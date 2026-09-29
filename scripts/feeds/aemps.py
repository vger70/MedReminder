#!/usr/bin/env python3
"""ES catalogue feed: AEMPS CIMA "Medicamentos" export.

Downloads https://listadomedicamentos.aemps.gob.es/Medicamentos.xls,
checks it and publishes data/es/aemps-<yyyymm>.zip with the file stored
unchanged as `aemps.xlsx`, the entry AempsCimaParser reads
(docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §3.2).

The server answers 403 to requests without browser headers, so the
download sends them. The file is labelled .xls but is an XLSX (a ZIP
container); anything else (HTML error page, legacy BIFF .xls) fails
the run instead of publishing a file the parser cannot read.
"""

import sys
import tempfile
from pathlib import Path

import openpyxl

import common

SOURCE_URL = "https://listadomedicamentos.aemps.gob.es/Medicamentos.xls"
REFERER = "https://cima.aemps.es/cima/publico/home.html"
COUNTRY = "ES"
PREFIX = "aemps"
DATA_DIR = Path("data/es")
ENTRY_NAME = "aemps.xlsx"

# The 15 columns of the first sheet, in order (docs/CATALOGUE-DATA.md §5,
# AempsCimaParser.ValidateHeader).
EXPECTED_HEADER = [
    "Nº Registro",
    "Medicamento",
    "Laboratorio",
    "Fecha Aut.",
    "Estado",
    "Fecha Estado",
    "Cód. ATC",
    "Principios Activos",
    "Nº P. Activos",
    "¿Comercializado?",
    "¿Triangulo Amarillo?",
    "Observaciones",
    "¿Sustituible?",
    "¿Afecta conducción?",
    "¿Problemas de suministro?",
]

# Absolute floor, well below the real size (26,763 data rows on
# 2026-09-29) but far above a truncated download.
MIN_DATA_ROWS = 20_000

ZIP_MAGIC = b"PK\x03\x04"
BIFF_MAGIC = b"\xd0\xcf\x11\xe0"


def check_payload(data):
    """Reject anything that is not an XLSX (ZIP) container."""
    if common.looks_like_html(data):
        raise common.FeedError("Medicamentos.xls: the server returned an HTML page")
    if data.startswith(BIFF_MAGIC):
        raise common.FeedError("Medicamentos.xls: legacy BIFF .xls, not the expected XLSX")
    if not data.startswith(ZIP_MAGIC):
        raise common.FeedError("Medicamentos.xls: not an XLSX (ZIP) container")


def _text(value):
    return "" if value is None else str(value).strip()


def read_workbook(path):
    """Header of the first sheet (trailing empty cells dropped) and the
    number of non-empty data rows below it."""
    workbook = openpyxl.load_workbook(path, read_only=True, data_only=True)
    try:
        rows = workbook.worksheets[0].iter_rows(values_only=True)
        header = [_text(cell) for cell in next(rows, ())]
        while header and not header[-1]:
            header.pop()
        count = sum(1 for row in rows if any(_text(cell) for cell in row))
    finally:
        workbook.close()
    return header, count


def check_header(header):
    if [h.casefold() for h in header] != [h.casefold() for h in EXPECTED_HEADER]:
        raise common.FeedError(
            f"{ENTRY_NAME}: header {header} does not match the expected {len(EXPECTED_HEADER)} columns"
        )


def validate(path, previous):
    """Check the saved workbook; return the manifest row counts."""
    header, count = read_workbook(path)
    check_header(header)
    common.check_rows(ENTRY_NAME, count, MIN_DATA_ROWS, previous.get(ENTRY_NAME))
    return {ENTRY_NAME: count}


def download(session):
    print(f"Download {SOURCE_URL}")
    response = session.get(SOURCE_URL, headers=common.browser_headers(REFERER), timeout=120)
    response.raise_for_status()
    check_payload(response.content)
    print(f"Downloaded {len(response.content):,} bytes")
    return response.content


def main():
    run = common.run_time()
    version = common.version_for(run)
    if common.already_published(DATA_DIR, version, common.force_refresh()):
        print(f"Version {version} is already published; nothing to do.")
        return 0

    payload = download(common.session())

    with tempfile.TemporaryDirectory() as work:
        # openpyxl checks the extension, not the content: the bytes are
        # saved unchanged under the name the parser reads.
        workbook = Path(work) / ENTRY_NAME
        workbook.write_bytes(payload)
        rows = validate(workbook, common.previous_rows(DATA_DIR))

        archive = common.write_zip(Path(work) / f"{PREFIX}-{version}.zip", {ENTRY_NAME: workbook})
        common.publish(DATA_DIR, COUNTRY, version, archive, 1, rows, run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
