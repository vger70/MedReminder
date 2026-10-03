#!/usr/bin/env python3
"""IT shortage feed: AIFA "Elenco dei farmaci carenti".

Downloads the list of medicines in temporary shortage, checks it and
publishes data/it/shortages/shortages-<yyyymmdd>.json (the list date)
with a latest.json manifest, the files the app reads
(docs/notes/EVOLUTION-PROPOSALS-2.md §3.3, docs/CATALOGUE-DATA.md).

The CSV is Windows-1252, starts with two free-text lines before the
header, uses ';', quotes fields that hold line breaks and doubled
quotes, and may list a code twice: it is read with a real CSV parser,
never line by line. Only what the app needs is published: the AIC code,
the start and expected end of the shortage, whether AIFA reports
equivalent medicines, and a category of the reason.

Usage: python scripts/feeds/aifa_shortages.py [--input FILE]
(--input publishes a file already downloaded instead of the AIFA one).
"""

import argparse
import csv
import io
import re
import sys
import tempfile
from datetime import date, datetime
from pathlib import Path

import common

CSV_URL = "https://www.aifa.gov.it/documents/20142/847339/elenco_medicinali_carenti.csv"
COUNTRY = "IT"
PREFIX = "shortages"
DATA_DIR = Path("data/it/shortages")
RETAINED_FILES = 3

AIC = "Codice AIC"
START = "Data inizio"
END = "Fine presunta"
EQUIVALENT = "Equivalente"
REASON = "Motivazioni"
REQUIRED_COLUMNS = ["Nome medicinale", AIC, START, END, EQUIVALENT, REASON]

# The list held 2,514 rows on 29/09/2026; a truncated download holds far
# fewer. Shortages also end, so the floor against the previous run is
# looser than for the catalogue.
MIN_ENTRIES = 300
MIN_SHARE_OF_PREVIOUS = 0.5

LIST_DATE = re.compile(r"aggiornato al\s+(\d{1,2})/(\d{1,2})/(\d{4})", re.IGNORECASE)

# Reason categories, checked in this order on the lower-case text: the
# first match wins ("Elevata richiesta/problemi produttivi" is a
# production problem).
REASONS = [
    ("withdrawn", "cessata commercializzazione definitiva"),
    ("suspended", "cessata commercializzazione temporanea"),
    ("production", "problemi produttivi"),
    ("production", "ridotta disponibilit"),
    ("demand", "elevata richiesta"),
    ("commercial", "motivi commerciali"),
    ("regulatory", "problemi regolatori"),
]


def decode(raw):
    return common.decode_text(raw)


def reason_category(text):
    lowered = (text or "").lower()
    for category, marker in REASONS:
        if marker in lowered:
            return category
    return "other"


def parse_date(text, field, aic):
    text = (text or "").strip()
    if not text:
        return None
    try:
        return datetime.strptime(text, "%d/%m/%Y").date()
    except ValueError as exc:
        raise common.FeedError(f"{aic}: {field} '{text}' is not dd/mm/yyyy") from exc


def parse(text):
    """Return (list date, entries sorted by AIC) from the CSV text."""
    header_at = text.find(REQUIRED_COLUMNS[0] + ";")
    if header_at < 0:
        raise common.FeedError("header row not found")
    match = LIST_DATE.search(text[:header_at])
    if not match:
        raise common.FeedError("list date ('aggiornato al dd/mm/yyyy') not found before the header")
    day, month, year = (int(g) for g in match.groups())
    list_date = date(year, month, day)

    reader = csv.DictReader(io.StringIO(text[header_at:], newline=""), delimiter=";")
    missing = [c for c in REQUIRED_COLUMNS if c not in (reader.fieldnames or [])]
    if missing:
        raise common.FeedError(f"missing columns {missing}")

    entries = {}
    for row in reader:
        aic = (row.get(AIC) or "").strip()
        if not aic:
            continue
        if not re.fullmatch(r"\d{9}", aic):
            raise common.FeedError(f"AIC '{aic}' is not 9 digits")
        start = parse_date(row.get(START), START, aic)
        if start is None:
            raise common.FeedError(f"{aic}: no start date")
        end = parse_date(row.get(END), END, aic)
        entry = {
            "aic": aic,
            "start": start.isoformat(),
            "expectedEnd": end.isoformat() if end else None,
            "equivalent": (row.get(EQUIVALENT) or "").strip().lower() in ("sì", "si", "yes"),
            "reason": reason_category(row.get(REASON)),
        }
        # Duplicated rows: the earliest start wins, as the shortage began then.
        previous = entries.get(aic)
        if previous is None or entry["start"] < previous["start"]:
            entries[aic] = entry
    return list_date, [entries[k] for k in sorted(entries)]


def check_count(count, previous):
    minimum = MIN_ENTRIES
    if isinstance(previous, int) and not isinstance(previous, bool) and previous > 0:
        minimum = max(minimum, int(previous * MIN_SHARE_OF_PREVIOUS))
    if count < minimum:
        raise common.FeedError(f"{count} entries, at least {minimum} required")
    print(f"Validated {count:,} entries (minimum {minimum:,})")


def build_document(list_date, entries, generated):
    return {
        "country": COUNTRY,
        "source": "AIFA",
        "listDate": list_date.isoformat(),
        "generated": generated.isoformat(),
        "entries": entries,
    }


def publish(data_dir, list_date, document, generated):
    """Write shortages-<yyyymmdd>.json, then latest.json; keep the newest files."""
    return common.publish_dated_list(data_dir, PREFIX, COUNTRY, list_date, document, generated,
                                     {"entries": len(document["entries"])}, RETAINED_FILES)


def download(session):
    print(f"Download {CSV_URL}")
    response = session.get(CSV_URL, headers=common.browser_headers(), timeout=120)
    response.raise_for_status()
    if common.looks_like_html(response.content):
        raise common.FeedError("the server returned HTML instead of CSV")
    return response.content


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--input", help="publish this CSV file instead of downloading it")
    args = parser.parse_args(argv)

    run = common.run_time()
    raw = Path(args.input).read_bytes() if args.input else download(common.session())
    list_date, entries = parse(decode(raw))

    manifest = common.read_manifest(DATA_DIR)
    if manifest.get("version") == f"{list_date:%Y%m%d}" and not common.force_refresh():
        print(f"List of {list_date} is already published; nothing to do.")
        return 0
    previous = manifest.get("rows", {}).get("entries") if isinstance(manifest.get("rows"), dict) else None
    check_count(len(entries), previous)

    publish(DATA_DIR, list_date, build_document(list_date, entries, run), run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
