#!/usr/bin/env python3
"""US catalogue feed: FDA National Drug Code Directory, via openFDA.

Reads the openFDA download manifest, downloads the bulk `drug/ndc` export
(zipped JSON, CC0 1.0), keeps the finished human prescription, OTC and
vaccine products, writes one row per package to `fda-ndc.tsv` and
publishes data/us/fda-ndc-<yyyymm>.zip with it, the layout
OpenFdaNdcParser reads (docs/analysis/ANALYSIS-CATALOGUE-US-GB-SOURCES.md
§3.2, §3.3).

Field names follow FDA's own openFDA pipeline (FDA/openfda,
openfda/ndc/pipeline.py and schemas/ndc_mapping.json).

TSV rules:
  * header row with the COLUMNS names, TAB delimiter, UTF-8 without BOM,
    `\\n` line ends, no quoting: TAB, CR and LF inside values become
    spaces and runs of spaces collapse;
  * `ndc` is the package NDC in the 12-digit 6-4-2 form of the FDA rule
    effective 2033-03-07 (each segment left-padded with zeros), stable
    across that change; `ndc_published` keeps the code as FDA lists it;
  * `status` is the marketing category, or "Discontinued (<category>,
    marketing ended <date>)" when the package or the product has a
    marketing end date on or before the run date;
  * `ingredients` holds the active ingredient names separated by `|`.
"""

import io
import json
import re
import sys
import tempfile
import zipfile
from datetime import date
from pathlib import Path
from urllib.parse import urlparse

import common

MANIFEST_URL = "https://api.fda.gov/download.json"
# Used when the manifest cannot be read or names no partition.
FALLBACK_URLS = ["https://download.open.fda.gov/drug/ndc/drug-ndc-0001-of-0001.json.zip"]
DOWNLOAD_HOST = "download.open.fda.gov"
COUNTRY = "US"
PREFIX = "fda-ndc"
DATA_DIR = Path("data/us")
ENTRY_NAME = "fda-ndc.tsv"

COLUMNS = [
    "ndc",
    "ndc_published",
    "name",
    "generic_name",
    "form",
    "dosage",
    "labeler",
    "status",
    "regime",
    "ingredients",
    "spl_set_id",
]

# Product types kept (PRODUCTTYPENAME in the FDA text files). Bulk
# ingredients, allergenics, cellular therapy, compounded and unfinished
# products are dropped.
KEPT_PRODUCT_TYPES = {
    "HUMAN PRESCRIPTION DRUG": "Rx",
    "HUMAN OTC DRUG": "OTC",
    "VACCINE": "Rx",
}

# Absolute floor for the package rows, far above a truncated download.
# Set before the first published run; raise it to about 80% of the
# first measured count (latest.json "rows") once it is known.
MIN_ROWS = 169_500

# Download caps: the manifest is a few kB, the NDC export about 27 MB
# zipped (openFDA, 2026-10).
MAX_MANIFEST_BYTES = 4 * 1024 * 1024
MAX_ARCHIVE_BYTES = 512 * 1024 * 1024

_WHITESPACE_RUN = re.compile(r"[\r\n\t ]+")
_SEGMENTS = re.compile(r"^(\d{4,6})-(\d{3,4})-(\d{1,2})$")
_UUID = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
_DATE = re.compile(r"^(\d{4})-?(\d{2})-?(\d{2})$")


def clean(value):
    """One value as TSV text."""
    if value is None:
        return ""
    return _WHITESPACE_RUN.sub(" ", str(value)).strip()


def canonical_ndc(published):
    """The 12-digit 6-4-2 form of a package NDC, or None if malformed.

    10-digit NDCs are 4-4-2, 5-3-2 or 5-4-1; the 2033 format is 6-4-2.
    Left-padding each segment is the conversion the FDA rule defines.
    """
    match = _SEGMENTS.match(clean(published))
    if not match:
        return None
    labeler, product, package = match.groups()
    if len(labeler) + len(product) + len(package) not in (10, 12):
        return None
    return f"{labeler.zfill(6)}-{product.zfill(4)}-{package.zfill(2)}"


def parse_date(value):
    """A date from `yyyymmdd` or `yyyy-mm-dd`, else None."""
    match = _DATE.match(clean(value))
    if not match:
        return None
    try:
        return date(int(match.group(1)), int(match.group(2)), int(match.group(3)))
    except ValueError:
        return None


def first_text(value):
    """The first non-empty string of a value that may be a list."""
    if isinstance(value, list):
        for item in value:
            text = clean(item)
            if text:
                return text
        return ""
    return clean(value)


def status_for(product, package, today):
    category = clean(product.get("marketing_category")) or "Listed"
    ends = [d for d in (parse_date(package.get("marketing_end_date")),
                        parse_date(product.get("marketing_end_date"))) if d is not None]
    ended = min(ends) if ends else None
    if ended is not None and ended <= today:
        return f"Discontinued ({category}, marketing ended {ended.isoformat()})"
    return category


def regime_for(product):
    regime = KEPT_PRODUCT_TYPES.get(clean(product.get("product_type")).upper(), "")
    schedule = clean(product.get("dea_schedule"))
    return f"{regime}, DEA {schedule}" if regime and schedule else regime


def ingredients_of(product):
    names = []
    strengths = []
    for ingredient in product.get("active_ingredients") or []:
        if not isinstance(ingredient, dict):
            continue
        name = clean(ingredient.get("name")).replace("|", "/")
        if name and name not in names:
            names.append(name)
        strength = clean(ingredient.get("strength"))
        if strength:
            strengths.append(strength)
    return names, strengths


def rows_from_products(products, today):
    """TSV rows (lists of strings) and the count of skipped packages."""
    rows = []
    skipped = 0
    seen = {}
    for product in products:
        if not isinstance(product, dict):
            continue
        if product.get("finished") is False:
            continue
        if clean(product.get("product_type")).upper() not in KEPT_PRODUCT_TYPES:
            continue

        name = clean(product.get("brand_name")) or clean(product.get("generic_name"))
        names, strengths = ingredients_of(product)
        openfda = product.get("openfda") if isinstance(product.get("openfda"), dict) else {}
        set_id = first_text(openfda.get("spl_set_id"))
        if not _UUID.match(set_id):
            set_id = ""

        for package in product.get("packaging") or []:
            if not isinstance(package, dict) or package.get("sample") is True:
                continue
            published = clean(package.get("package_ndc"))
            ndc = canonical_ndc(published)
            if ndc is None or not name:
                skipped += 1
                continue
            if ndc in seen:
                # The same code listed twice is skipped; two codes that
                # become one 12-digit NDC would make the key ambiguous.
                if seen[ndc] != published:
                    raise common.FeedError(
                        f"{ENTRY_NAME}: {published} and {seen[ndc]} have the same 12-digit NDC {ndc}")
                skipped += 1
                continue
            seen[ndc] = published

            dosage = " — ".join(part for part in ("; ".join(strengths), clean(package.get("description"))) if part)
            rows.append([
                ndc,
                published,
                name,
                clean(product.get("generic_name")),
                clean(product.get("dosage_form")),
                dosage,
                clean(product.get("labeler_name")),
                status_for(product, package, today),
                regime_for(product),
                "|".join(names),
                set_id,
            ])
    rows.sort(key=lambda row: row[0])
    return rows, skipped


def write_tsv(path, rows):
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\t".join(COLUMNS) + "\n")
        for row in rows:
            fh.write("\t".join(row) + "\n")


def validate(tsv_path, previous):
    """Re-read the TSV as the parser will; return the manifest row counts."""
    with open(tsv_path, encoding="utf-8", newline="") as fh:
        lines = fh.read().split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    if not lines or lines[0].split("\t") != COLUMNS:
        raise common.FeedError(f"{ENTRY_NAME}: unexpected header")

    seen = set()
    for number, line in enumerate(lines[1:], start=2):
        fields = line.split("\t")
        if len(fields) != len(COLUMNS):
            raise common.FeedError(f"{ENTRY_NAME}: line {number} has {len(fields)} columns, {len(COLUMNS)} expected")
        if not re.match(r"^\d{6}-\d{4}-\d{2}$", fields[0]) or not fields[2]:
            raise common.FeedError(f"{ENTRY_NAME}: line {number} has no valid NDC or name")
        if fields[0] in seen:
            raise common.FeedError(f"{ENTRY_NAME}: line {number} repeats NDC {fields[0]}")
        seen.add(fields[0])

    return {ENTRY_NAME: common.check_rows(ENTRY_NAME, len(lines) - 1, MIN_ROWS, previous.get(ENTRY_NAME))}


def partition_urls(manifest):
    """The `drug/ndc` partition URLs of the openFDA download manifest."""
    try:
        partitions = manifest["results"]["drug"]["ndc"]["partitions"]
    except (KeyError, TypeError):
        return []
    urls = []
    for partition in partitions if isinstance(partitions, list) else []:
        url = partition.get("file") if isinstance(partition, dict) else None
        if isinstance(url, str) and urlparse(url).scheme == "https" and urlparse(url).hostname == DOWNLOAD_HOST:
            urls.append(url)
    return urls


def products_of(archive_bytes):
    """The product objects of one zipped openFDA JSON export."""
    if common.looks_like_html(archive_bytes):
        raise common.FeedError("the server returned an HTML page instead of the export")
    try:
        with zipfile.ZipFile(io.BytesIO(archive_bytes)) as zf:
            names = [name for name in zf.namelist() if name.lower().endswith(".json")]
            if not names:
                raise common.FeedError("the export holds no JSON file")
            products = []
            for name in names:
                with zf.open(name) as fh:
                    document = json.load(fh)
                results = document.get("results") if isinstance(document, dict) else document
                if not isinstance(results, list):
                    raise common.FeedError(f"{name}: no 'results' list")
                products.extend(results)
            return products
    except zipfile.BadZipFile as error:
        raise common.FeedError(f"the export is not a ZIP archive ({error})") from error


def get_bytes(session, url, limit):
    response = session.get(url, headers={"User-Agent": common.BROWSER_USER_AGENT}, timeout=300)
    response.raise_for_status()
    if len(response.content) > limit:
        raise common.FeedError(f"{url}: {len(response.content):,} bytes, more than {limit:,}")
    return response.content


def download(session):
    print(f"Read {MANIFEST_URL}")
    urls = []
    try:
        urls = partition_urls(json.loads(get_bytes(session, MANIFEST_URL, MAX_MANIFEST_BYTES)))
    except (OSError, ValueError, common.FeedError) as error:
        # requests' exceptions derive from OSError.
        print(f"Manifest unreadable ({error})")
    if not urls:
        print(f"No drug/ndc partition in the manifest; using {FALLBACK_URLS}")
        urls = FALLBACK_URLS

    products = []
    for url in urls:
        print(f"Download {url}")
        data = get_bytes(session, url, MAX_ARCHIVE_BYTES)
        print(f"Downloaded {len(data):,} bytes")
        products.extend(products_of(data))
    print(f"Read {len(products):,} products")
    return products


def main():
    run = common.run_time()
    version = common.version_for(run)
    if common.already_published(DATA_DIR, version, common.force_refresh()):
        print(f"Version {version} is already published; nothing to do.")
        return 0

    products = download(common.session())
    rows, skipped = rows_from_products(products, run.date())
    print(f"Kept {len(rows):,} packages; skipped {skipped:,} without a valid NDC or name, or listed twice")

    with tempfile.TemporaryDirectory() as work:
        tsv_path = Path(work) / ENTRY_NAME
        write_tsv(tsv_path, rows)
        counts = validate(tsv_path, common.previous_rows(DATA_DIR))

        archive = common.write_zip(Path(work) / f"{PREFIX}-{version}.zip", {ENTRY_NAME: tsv_path})
        common.publish(DATA_DIR, COUNTRY, version, archive, 1, counts, run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
