#!/usr/bin/env python3

import os
from datetime import datetime
import zipfile
from urllib.parse import urljoin
from pathlib import Path
import shutil
import requests
from bs4 import BeautifulSoup

PAGE_URL = "https://www.aifa.gov.it/liste-dei-farmaci"
OUTPUT_DIR = "aifa_csv"
ZIP_NAME = f"aifa-{datetime.now():%Y%m}.zip"

os.makedirs(OUTPUT_DIR, exist_ok=True)

headers = {
    "User-Agent": (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) "
        "Chrome/130.0 Safari/537.36"
    )
}

print(f"Leggo {PAGE_URL}")

resp = requests.get(PAGE_URL, headers=headers, timeout=60)
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

    r = requests.get(
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
MIN_DATA_ROWS = 1000


def validate_csv(path, required):
    with open(path, encoding="latin-1", newline="") as fh:
        header = fh.readline().strip()
        columns = {c.strip().strip('"').upper() for c in header.split(";")}
        missing = [c for c in required if c not in columns]
        if missing:
            raise RuntimeError(
                f"{os.path.basename(path)}: missing columns {missing}"
            )
        rows = sum(1 for line in fh if line.strip())
    if rows < MIN_DATA_ROWS:
        raise RuntimeError(
            f"{os.path.basename(path)}: {rows} data rows, "
            f"at least {MIN_DATA_ROWS} required"
        )
    print(f"Validated {os.path.basename(path)}: {rows:,} data rows")


by_name = {os.path.basename(f).lower(): f for f in downloaded_files}
for name, required in REQUIRED_COLUMNS.items():
    path = by_name.get(name.lower())
    if path is None:
        raise RuntimeError(f"{name} was not downloaded")
    validate_csv(path, required)

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
import json
from datetime import datetime, timezone

# sha256 and size let the app verify the archive it downloads
# (docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEED.md §5.3).
sha256 = hashlib.sha256()
with open(ZIP_NAME, "rb") as fh:
    for chunk in iter(lambda: fh.read(1024 * 1024), b""):
        sha256.update(chunk)

latest_info = {
    "version": datetime.now().strftime("%Y%m"),
    "file": ZIP_NAME,
    "generated": datetime.now(timezone.utc).isoformat(),
    "csv_count": len(csv_links),
    "sha256": sha256.hexdigest(),
    "size": os.path.getsize(ZIP_NAME),
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
