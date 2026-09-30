#!/usr/bin/env python3
"""IT catalogue feed: AIFA "Liste dei farmaci".

Scrapes the AIFA page for confezioni_fornitura.csv and
PA_confezioni.csv, checks them and publishes data/it/aifa-<yyyymm>.zip
with both files stored unchanged, the layout AifaSnapshotParser reads
(docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md).
"""

import os
import sys
import tempfile
from pathlib import Path
from urllib.parse import urljoin

from bs4 import BeautifulSoup

import common

PAGE_URL = "https://www.aifa.gov.it/liste-dei-farmaci"
COUNTRY = "IT"
PREFIX = "aifa"
DATA_DIR = Path("data/it")

CONFEZIONI = "confezioni_fornitura.csv"
PA = "PA_confezioni.csv"

# Validated before anything under data/ changes: the app imports
# whatever latest.json points to, and a header-only or truncated file
# would reach every user (ANALYSIS-CATALOGUE-REMOTE-FEED.md §2.2 W2, §8).
REQUIRED_COLUMNS = {
    CONFEZIONI: [
        "CODICE_AIC", "DENOMINAZIONE", "DESCRIZIONE", "RAGIONE_SOCIALE",
        "STATO_AMMINISTRATIVO", "TIPO_PROCEDURA", "FORMA", "CODICE_ATC",
        "FORNITURA", "LINK_FI", "LINK_RCP",
    ],
    PA: ["CODICE_AIC", "PRINCIPIO_ATTIVO"],
}

# Absolute floors, well below the real sizes (September 2026: 160,024
# and 338,722 data rows) but far above a truncated download.
MIN_DATA_ROWS = {
    CONFEZIONI: 100_000,
    PA: 200_000,
}


def find_links(html, page_url=PAGE_URL):
    """Absolute, de-duplicated and sorted URLs of the two CSV files."""
    soup = BeautifulSoup(html, "html.parser")
    links = [
        urljoin(page_url, anchor["href"])
        for anchor in soup.find_all("a", href=True)
        if any(name in anchor["href"] for name in REQUIRED_COLUMNS)
    ]
    return sorted(dict.fromkeys(links))


def validate_csv(path, required, minimum, previous=None):
    """Check the header columns and count the non-empty data lines."""
    name = os.path.basename(path)
    with open(path, encoding="latin-1", newline="") as fh:
        header = fh.readline().strip()
        columns = {c.strip().strip('"').upper() for c in header.split(";")}
        missing = [c for c in required if c not in columns]
        if missing:
            raise common.FeedError(f"{name}: missing columns {missing}")
        rows = sum(1 for line in fh if line.strip())
    return common.check_rows(name, rows, minimum, previous)


def validate(paths, previous):
    """`paths` maps downloaded file names to paths; return the row counts."""
    by_name = {name.lower(): path for name, path in paths.items()}
    counts = {}
    for name, required in REQUIRED_COLUMNS.items():
        path = by_name.get(name.lower())
        if path is None:
            raise common.FeedError(f"{name} was not downloaded")
        counts[name] = validate_csv(path, required, MIN_DATA_ROWS[name], previous.get(name))
    return counts


def download(session, work):
    # Only the browser User-Agent, as the script always sent: AIFA
    # needs nothing more.
    headers = {"User-Agent": common.BROWSER_USER_AGENT}
    print(f"Read {PAGE_URL}")
    page = session.get(PAGE_URL, headers=headers, timeout=60)
    page.raise_for_status()

    links = find_links(page.text)
    if not links:
        raise common.FeedError("no CSV link found on the AIFA page")

    paths = {}
    for url in links:
        name = os.path.basename(url)
        print(f"Download {url}")
        response = session.get(url, headers=headers, timeout=120, allow_redirects=True)
        response.raise_for_status()
        if "html" in response.headers.get("Content-Type", "").lower():
            raise common.FeedError(f"{name}: the server returned HTML instead of CSV")
        target = Path(work) / name
        target.write_bytes(response.content)
        if len(response.content) < 100:
            print(f"WARNING: {name} is very small ({len(response.content)} bytes)")
        paths[name] = target
    return paths


def main():
    run = common.run_time()
    version = common.version_for(run)
    if common.already_published(DATA_DIR, version, common.force_refresh()):
        print(f"Version {version} is already published; nothing to do.")
        return 0

    with tempfile.TemporaryDirectory() as work:
        paths = download(common.session(), work)
        rows = validate(paths, common.previous_rows(DATA_DIR))

        archive = common.write_zip(Path(work) / f"{PREFIX}-{version}.zip", paths)
        common.publish(DATA_DIR, COUNTRY, version, archive, len(paths), rows, run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
