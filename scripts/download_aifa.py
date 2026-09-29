#!/usr/bin/env python3

import os
from datetime import datetime, timezone
import zipfile
from urllib.parse import urljoin
from pathlib import Path
import json
import shutil
import sys
import requests
from bs4 import BeautifulSoup
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

PAGE_URL = "https://www.aifa.gov.it/liste-dei-farmaci"
OUTPUT_DIR = "aifa_csv"
# One timestamp for the whole run: the archive name, the manifest
# version and "generated" must agree even when a run crosses a month
# boundary, otherwise every client rejects the manifest.
RUN_TIME = datetime.now(timezone.utc)
VERSION = f"{RUN_TIME:%Y%m}"
ZIP_NAME = f"aifa-{VERSION}.zip"

# The workflow runs daily from the 2nd to the 7th of the month so that
# an AIFA outage on one day is retried the next. Once this month's
# version is published the remaining runs stop here, so each month is
# published once. FORCE_REFRESH=true (workflow_dispatch input) rebuilds
# the month; clients re-import it because the new "generated" and
# "sha256" make it a later build (ANALYSIS-CATALOGUE-REMOTE-FEED.md
# §11.2).
FORCE_REFRESH = os.getenv("FORCE_REFRESH", "").strip().lower() == "true"
try:
    with open("data/latest.json", encoding="utf-8") as fh:
        published_version = json.load(fh).get("version")
except (OSError, ValueError):
    published_version = None

if published_version == VERSION and not FORCE_REFRESH:
    print(f"Version {VERSION} is already published; nothing to do.")
    sys.exit(0)

os.makedirs(OUTPUT_DIR, exist_ok=True)

# Transient AIFA errors (502 Bad Gateway, 503, timeouts, resets) are
# retried up to 5 times with exponential back-off (urllib3 waits 0, 30,
# 60, 120 and 120 s: about 5.5 minutes in total). Retry-After is
# honoured when the server sends it. A persistent error still fails the
# run, and the next day's scheduled run tries again.
RETRY = Retry(
    total=5,
    connect=5,
    read=5,
    status=5,
    backoff_factor=15,
    backoff_max=120,
    status_forcelist=(429, 500, 502, 503, 504),
    allowed_methods=frozenset({"GET"}),
    respect_retry_after_header=True,
    raise_on_status=False,
)
session = requests.Session()
session.mount("https://", HTTPAdapter(max_retries=RETRY))
session.mount("http://", HTTPAdapter(max_retries=RETRY))

headers = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/130.0 Safari/537.36"
    )
}

print(f"Leggo {PAGE_URL}")

resp = session.get(PAGE_URL, headers=headers, timeout=60)
resp.raise_for_status()

soup = BeautifulSoup(resp.text, "html.parser")

csv_links = []

for a in soup.find_all("a", href=True):
    href = a["href"]

    if (
        "confezioni_fornitura.csv" in href
        or "PA_confezioni.csv" in href
    ):
        csv_links.append(urljoin(PAGE_URL, href))

csv_links = list(dict.fromkeys(csv_links))
csv_links.sort()

if not csv_links:
    raise RuntimeError("Nessun CSV trovato")

print("\nCSV individuati:")
for link in csv_links:
    print(" -", link)

downloaded_files = []

for url in csv_links:

    filename = os.path.basename(url)
    target = os.path.join(OUTPUT_DIR, filename)

    print(f"\nDownload {filename}")

    r = session.get(
        url,
        headers=headers,
        timeout=120,
        allow_redirects=True,
    )

    r.raise_for_status()

    content_type = r.headers.get("Content-Type", "")

    if "html" in content_type.lower():
        raise RuntimeError(
            f"{filename}: il server ha restituito HTML "
            f"invece di CSV"
        )

    with open(target, "wb") as f:
        f.write(r.content)

    if os.path.getsize(target) < 100:
        print(
            f"ATTENZIONE: file molto piccolo "
            f"({os.path.getsize(target)} byte)"
        )

    downloaded_files.append(target)

# Validate the CSVs before anything under data/ changes: the app
# imports whatever latest.json points to, and a header-only or
# truncated file would reach every user
# (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §2.2 W2, §8).
REQUIRED_COLUMNS = {
    "confezioni_fornitura.csv": [
        "CODICE_AIC", "DENOMINAZIONE", "DESCRIZIONE", "RAGIONE_SOCIALE",
        "STATO_AMMINISTRATIVO", "TIPO_PROCEDURA", "FORMA", "CODICE_ATC",
        "FORNITURA", "LINK_FI", "LINK_RCP",
    ],
    "PA_confezioni.csv": ["CODICE_AIC", "PRINCIPIO_ATTIVO"],
}
# Absolute floors, well below the real sizes (September 2026: 160,024
# and 338,722 data rows) but far above a truncated download.
MIN_DATA_ROWS = {
    "confezioni_fornitura.csv": 100_000,
    "PA_confezioni.csv": 200_000,
}
# A file may not shrink below this share of the previous published run
# (the row counts recorded in data/latest.json).
MIN_SHARE_OF_PREVIOUS = 0.9


def previous_row_counts():
    try:
        with open("data/latest.json", encoding="utf-8") as fh:
            rows = json.load(fh).get("rows", {})
    except (OSError, ValueError):
        return {}
    return rows if isinstance(rows, dict) else {}


def validate_csv(path, required, minimum):
    with open(path, encoding="latin-1", newline="") as fh:
        header = fh.readline().strip()
        columns = {c.strip().strip('"').upper() for c in header.split(";")}
        missing = [c for c in required if c not in columns]
        if missing:
            raise RuntimeError(
                f"{os.path.basename(path)}: missing columns {missing}"
            )
        rows = sum(1 for line in fh if line.strip())
    if rows < minimum:
        raise RuntimeError(
            f"{os.path.basename(path)}: {rows} data rows, "
            f"at least {minimum} required"
        )
    print(f"Validated {os.path.basename(path)}: {rows:,} data rows")
    return rows


previous_rows = previous_row_counts()
row_counts = {}
by_name = {os.path.basename(f).lower(): f for f in downloaded_files}
for name, required in REQUIRED_COLUMNS.items():
    path = by_name.get(name.lower())
    if path is None:
        raise RuntimeError(f"{name} was not downloaded")
    minimum = MIN_DATA_ROWS[name]
    previous = previous_rows.get(name)
    if isinstance(previous, int) and previous > 0:
        minimum = max(minimum, int(previous * MIN_SHARE_OF_PREVIOUS))
    row_counts[name] = validate_csv(path, required, minimum)

print("\nCreazione ZIP")

with zipfile.ZipFile(
    ZIP_NAME,
    "w",
    compression=zipfile.ZIP_DEFLATED
) as z:

    for f in downloaded_files:
        z.write(f, arcname=os.path.basename(f))

print(f"\nCreato: {ZIP_NAME}")

print("\nContenuto ZIP:")
for f in downloaded_files:
    print(
        f" - {os.path.basename(f)} "
        f"({os.path.getsize(f):,} byte)"
    )
    
import hashlib

# sha256 and size let the app verify the archive it downloads
# (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §5.3).
sha256 = hashlib.sha256()
with open(ZIP_NAME, "rb") as fh:
    for chunk in iter(lambda: fh.read(1024 * 1024), b""):
        sha256.update(chunk)

latest_info = {
    "version": VERSION,
    "file": ZIP_NAME,
    "generated": RUN_TIME.isoformat(),
    "csv_count": len(csv_links),
    "sha256": sha256.hexdigest(),
    "size": os.path.getsize(ZIP_NAME),
    "rows": row_counts,
}

os.makedirs("data", exist_ok=True)

with open("data/latest.json", "w", encoding="utf-8") as f:
    json.dump(latest_info, f, indent=2)

# sposta lo zip nella cartella data
shutil.move(ZIP_NAME, f"data/{ZIP_NAME}")
ZIP_NAME = f"data/{ZIP_NAME}"

archives = sorted(Path("data").glob("aifa-*.zip"))

while len(archives) > 3:
    archives[0].unlink()
    archives.pop(0)
    
github_output = os.getenv("GITHUB_OUTPUT")

if github_output:
    with open(github_output, "a") as fh:
        fh.write(f"zip_name={ZIP_NAME}\n")
