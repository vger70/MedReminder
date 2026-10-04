#!/usr/bin/env python3
"""IT equivalents feed: AIFA "Lista di trasparenza" (equivalent medicines).

Downloads the monthly list of off-patent class A medicines with at
least one equivalent, checks it and publishes
data/it/equivalents/equivalents-<yyyymmdd>.json (the list date) with a
latest.json manifest, the files the app reads
(docs/analysis/ANALYSIS-IT-EQUIVALENTS-AND-INFO-LINK.md §2,
docs/CATALOGUE-DATA.md §9).

The CSV is Windows-1252 with ';' separators. Its AIC column lost the
leading zeros (7 or 8 digits): codes are left-padded to 9 digits, the
package code of the catalogue. The public-price column carries the list
date in its name ("Prezzo Pubblico 15 settembre 2026"): it is matched by
prefix and the date read from it. Prices are text with comma decimals
and a euro sign ("5,63 €") and are published in cents. The note of a
package is kept verbatim: it can restrict substitution inside the
group.

Usage: python scripts/feeds/aifa_equivalents.py [--input FILE]
(--input publishes a file already downloaded instead of the AIFA one).
"""

import argparse
import csv
import io
import re
import sys
from datetime import date
from pathlib import Path

import common

CSV_URL = "https://www.aifa.gov.it/documents/20142/825643/Lista_farmaci_equivalenti.csv"
COUNTRY = "IT"
PREFIX = "equivalents"
DATA_DIR = Path("data/it/equivalents")
RETAINED_FILES = 3

INGREDIENT = "Principio attivo"
REFERENCE = "Confezione di riferimento"
ATC = "ATC"
AIC = "AIC"
NAME = "Farmaco"
PACKAGE = "Confezione"
HOLDER = "Ditta"
REFERENCE_PRICE = "Prezzo riferimento SSN"
PUBLIC_PRICE_PREFIX = "Prezzo Pubblico"
DIFFERENCE = "Differenza"
NOTE = "Nota"
GROUP = "Codice gruppo equivalenza"
REQUIRED_COLUMNS = [INGREDIENT, REFERENCE, ATC, AIC, NAME, PACKAGE, HOLDER,
                    REFERENCE_PRICE, DIFFERENCE, NOTE, GROUP]

# The list held 8,560 packages in 1,010 groups on 15/09/2026; a
# truncated download holds far fewer. Off-patent medicines are added
# more often than removed, so the floor against the previous run is the
# catalogue one (90%).
MIN_PACKAGES = 4000

MONTHS = {
    "gennaio": 1, "febbraio": 2, "marzo": 3, "aprile": 4, "maggio": 5, "giugno": 6,
    "luglio": 7, "agosto": 8, "settembre": 9, "ottobre": 10, "novembre": 11, "dicembre": 12,
}
LIST_DATE = re.compile(r"(\d{1,2})\s+([a-z]+)\s+(\d{4})", re.IGNORECASE)
PRICE = re.compile(r"^-?\d{1,3}(?:\.?\d{3})*(?:,\d{1,2})?$")


def decode(raw):
    return common.decode_text(raw)


def list_date_from(column):
    """The date in 'Prezzo Pubblico 15 settembre 2026'."""
    match = LIST_DATE.search(column[len(PUBLIC_PRICE_PREFIX):])
    month = MONTHS.get(match.group(2).lower()) if match else None
    if not match or month is None:
        raise common.FeedError(f"no list date in the price column '{column}'")
    try:
        return date(int(match.group(3)), month, int(match.group(1)))
    except ValueError as exc:
        raise common.FeedError(f"invalid list date in the price column '{column}'") from exc


def price_cents(text, field, aic):
    """'5,63 €' -> 563; an empty cell -> None."""
    text = (text or "").replace("€", "").replace(" ", " ").strip()
    if not text:
        return None
    if not PRICE.match(text):
        raise common.FeedError(f"{aic}: {field} '{text}' is not a price")
    euros, _, cents = text.replace(".", "").partition(",")
    sign = -1 if euros.startswith("-") else 1
    return sign * (abs(int(euros)) * 100 + int((cents + "00")[:2]))


def pad_aic(text):
    """Left-pad the code to 9 digits; reject anything else."""
    text = (text or "").strip()
    if not re.fullmatch(r"\d{1,9}", text):
        raise common.FeedError(f"AIC '{text}' is not a code of up to 9 digits")
    return text.zfill(9)


def clean(text):
    return " ".join((text or "").split())


def parse(text):
    """Return (list date, groups sorted by code) from the CSV text."""
    header_at = text.find(INGREDIENT + ";")
    if header_at < 0:
        raise common.FeedError("header row not found")
    reader = csv.DictReader(io.StringIO(text[header_at:], newline=""), delimiter=";")
    fields = [f.strip() for f in (reader.fieldnames or [])]
    reader.fieldnames = fields
    missing = [c for c in REQUIRED_COLUMNS if c not in fields]
    price_columns = [f for f in fields if f.startswith(PUBLIC_PRICE_PREFIX)]
    if missing or len(price_columns) != 1:
        raise common.FeedError(f"missing columns {missing + ([] if len(price_columns) == 1 else [PUBLIC_PRICE_PREFIX])}")
    price_column = price_columns[0]
    list_date = list_date_from(price_column)

    groups = {}
    group_of = {}
    for row in reader:
        if not (row.get(AIC) or "").strip():
            continue
        aic = pad_aic(row.get(AIC))
        code = clean(row.get(GROUP))
        if not code:
            raise common.FeedError(f"{aic}: no equivalence group")
        if aic in group_of:
            if group_of[aic] != code:
                raise common.FeedError(f"{aic} is listed in groups {group_of[aic]} and {code}")
            continue
        group_of[aic] = code

        group = groups.get(code)
        if group is None:
            group = groups[code] = {
                "code": code,
                "ingredient": clean(row.get(INGREDIENT)),
                "reference": clean(row.get(REFERENCE)),
                "atc": clean(row.get(ATC)) or None,
                "referencePrice": price_cents(row.get(REFERENCE_PRICE), REFERENCE_PRICE, aic),
                "members": [],
            }
        note = (row.get(NOTE) or "").strip()
        group["members"].append({
            "aic": aic,
            "name": clean(row.get(NAME)),
            "package": clean(row.get(PACKAGE)),
            "holder": clean(row.get(HOLDER)),
            "price": price_cents(row.get(price_column), price_column, aic),
            "difference": price_cents(row.get(DIFFERENCE), DIFFERENCE, aic),
            "note": note or None,
        })

    for group in groups.values():
        group["members"].sort(key=lambda m: m["aic"])
    return list_date, [groups[k] for k in sorted(groups)]


def package_count(groups):
    return sum(len(g["members"]) for g in groups)


def check_count(count, previous):
    common.check_rows("packages", count, MIN_PACKAGES, previous)


def build_document(list_date, groups, generated):
    return {
        "country": COUNTRY,
        "source": "AIFA",
        "listDate": list_date.isoformat(),
        "generated": generated.isoformat(),
        "groups": groups,
    }


def publish(data_dir, list_date, document, generated, source=None):
    """Write equivalents-<yyyymmdd>.json, then latest.json; keep the newest files."""
    return common.publish_dated_list(data_dir, PREFIX, COUNTRY, list_date, document, generated,
                                     {"groups": len(document["groups"]), "packages": package_count(document["groups"])},
                                     RETAINED_FILES, source)


def download(session, force=False):
    """(bytes, source validators), or (None, None) when AIFA reports the
    file unchanged since the published list."""
    print(f"Download {CSV_URL}")
    response = common.conditional_get(session, CSV_URL, common.browser_headers(), DATA_DIR, force)
    if response is None:
        return None, None
    if common.looks_like_html(response.content):
        raise common.FeedError("the server returned HTML instead of CSV")
    return response.content, common.source_of(response)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--input", help="publish this CSV file instead of downloading it")
    args = parser.parse_args(argv)

    run = common.run_time()
    force = common.force_refresh()
    if args.input:
        raw, source = Path(args.input).read_bytes(), None
    else:
        raw, source = download(common.session(), force)
        if raw is None:
            print("AIFA reports the file unchanged since the published list; nothing to do.")
            return 0
    list_date, groups = parse(decode(raw))
    document = build_document(list_date, groups, run)

    skip = common.dated_list_skip_reason(DATA_DIR, list_date, document, force)
    if skip:
        if common.record_source(DATA_DIR, source):
            print("Recorded the new validators of the AIFA file.")
        print(f"{skip[0].upper()}{skip[1:]}; nothing to do.")
        return 0
    manifest = common.read_manifest(DATA_DIR)
    previous = manifest.get("rows", {}).get("packages") if isinstance(manifest.get("rows"), dict) else None
    check_count(package_count(groups), previous)

    publish(DATA_DIR, list_date, document, run, source)
    return 0


if __name__ == "__main__":
    sys.exit(main())
