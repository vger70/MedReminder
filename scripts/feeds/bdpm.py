#!/usr/bin/env python3
"""FR catalogue feed: ANSM "Base de données publique des médicaments".

Downloads CIS_bdpm.txt, CIS_CIP_bdpm.txt and CIS_COMPO_bdpm.txt, checks
them and publishes data/fr/bdpm-<yyyymm>.zip with the three files stored
unchanged, the layout AnsmBdpmParser reads
(docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §3.3).

The file links are scraped from the download page; when a link is
missing the script falls back to the /download/file/<name> pattern the
portal uses since 2026 (the older telechargement.php URLs answer 404).
"""

import sys
import tempfile
from pathlib import Path
from urllib.parse import urljoin

from bs4 import BeautifulSoup

import common

PAGE_URL = "https://base-donnees-publique.medicaments.gouv.fr/telechargement"
FALLBACK_URL = "https://base-donnees-publique.medicaments.gouv.fr/download/file/{name}"
COUNTRY = "FR"
PREFIX = "bdpm"
DATA_DIR = Path("data/fr")

CIS = "CIS_bdpm.txt"
CIP = "CIS_CIP_bdpm.txt"
COMPO = "CIS_COMPO_bdpm.txt"

# Per file: tab-separated column count of every line, accepted
# encodings (the parser decodes CIS and COMPO as Windows-1252; CIP is
# UTF-8 upstream and not read by the parser) and the absolute row floor
# (runner measurement of 2026-09-29: 15,883 / 20,862 / 32,439).
FILES = {
    CIS: {"columns": 12, "encodings": ("cp1252",), "min_rows": 12_000},
    CIP: {"columns": 13, "encodings": ("utf-8", "cp1252"), "min_rows": 15_000},
    COMPO: {"columns": 8, "encodings": ("cp1252",), "min_rows": 25_000},
}

# AnsmBdpmParser's shape check: column 4 of the first CIS row is the
# "Statut administratif AMM", which starts with this text.
STATUT_COLUMN = 4
STATUT_PREFIX = "Autorisation"


def find_links(html, page_url=PAGE_URL):
    """Absolute URL of each file, from the download page when linked
    there, from the fallback pattern otherwise."""
    soup = BeautifulSoup(html, "html.parser")
    links = {}
    for anchor in soup.find_all("a", href=True):
        href = anchor["href"].split("?", 1)[0].split("#", 1)[0]
        for name in FILES:
            # The whole last path segment must match the file name.
            if href.rsplit("/", 1)[-1] == name and name not in links:
                links[name] = urljoin(page_url, anchor["href"])
    for name in FILES:
        links.setdefault(name, FALLBACK_URL.format(name=name))
    return links


def decode(name, data):
    for encoding in FILES[name]["encodings"]:
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    raise common.FeedError(f"{name}: not decodable as {' or '.join(FILES[name]['encodings'])}")


def check_file(name, data, previous):
    """Check one file's bytes; return its row count."""
    if common.looks_like_html(data):
        raise common.FeedError(f"{name}: the server returned an HTML page")

    expected = FILES[name]["columns"]
    lines = [line for line in decode(name, data).splitlines() if line.strip()]
    for number, line in enumerate(lines, start=1):
        columns = line.count("\t") + 1
        if columns != expected:
            raise common.FeedError(f"{name}: line {number} has {columns} columns, {expected} expected")

    if name == CIS and lines:
        statut = lines[0].split("\t")[STATUT_COLUMN].strip()
        if not statut.startswith(STATUT_PREFIX):
            raise common.FeedError(
                f"{name}: column {STATUT_COLUMN} of the first row does not start with '{STATUT_PREFIX}'"
            )

    return common.check_rows(name, len(lines), FILES[name]["min_rows"], previous.get(name))


def validate(files, previous):
    """`files` maps each file name to its bytes; return the row counts."""
    missing = [name for name in FILES if name not in files]
    if missing:
        raise common.FeedError(f"missing files {missing}")
    return {name: check_file(name, files[name], previous) for name in FILES}


def download(session):
    headers = common.browser_headers(PAGE_URL)
    print(f"Read {PAGE_URL}")
    page = session.get(PAGE_URL, headers=headers, timeout=60)
    page.raise_for_status()
    links = find_links(page.text, page.url or PAGE_URL)

    files = {}
    for name, url in links.items():
        print(f"Download {url}")
        response = session.get(url, headers=headers, timeout=120)
        response.raise_for_status()
        if "html" in response.headers.get("Content-Type", "").lower():
            raise common.FeedError(f"{name}: the server returned HTML instead of the file")
        files[name] = response.content
        print(f"Downloaded {name}: {len(response.content):,} bytes")
    return files


def main():
    run = common.run_time()
    version = common.version_for(run)
    if common.already_published(DATA_DIR, version, common.force_refresh()):
        print(f"Version {version} is already published; nothing to do.")
        return 0

    files = download(common.session())
    rows = validate(files, common.previous_rows(DATA_DIR))

    with tempfile.TemporaryDirectory() as work:
        entries = {}
        for name, data in files.items():
            path = Path(work) / name
            path.write_bytes(data)
            entries[name] = path
        archive = common.write_zip(Path(work) / f"{PREFIX}-{version}.zip", entries)
        common.publish(DATA_DIR, COUNTRY, version, archive, len(entries), rows, run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
