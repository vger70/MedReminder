import shutil

import openpyxl
import pytest

import aemps
import common
from conftest import FIXTURES

SAMPLE = FIXTURES / "aemps-cima-sample.xlsx"


def test_check_payload_accepts_an_xlsx_container():
    aemps.check_payload(SAMPLE.read_bytes())


@pytest.mark.parametrize(
    "data,message",
    [
        (b"<html><body>403 Forbidden</body></html>", "HTML"),
        (b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1rest", "BIFF"),
        ("Nº Registro;Medicamento".encode("utf-8"), "not an XLSX"),
    ],
)
def test_check_payload_rejects_anything_else(data, message):
    with pytest.raises(common.FeedError, match=message):
        aemps.check_payload(data)


def test_read_workbook_returns_the_header_and_the_data_rows_of_the_sample():
    header, count = aemps.read_workbook(SAMPLE)
    assert header == aemps.EXPECTED_HEADER
    assert count == 145


def test_validate_counts_the_rows_of_the_sample(tmp_path, monkeypatch):
    monkeypatch.setattr(aemps, "MIN_DATA_ROWS", 100)
    target = tmp_path / aemps.ENTRY_NAME
    shutil.copy(SAMPLE, target)

    assert aemps.validate(target, {}) == {"aemps.xlsx": 145}


def test_validate_applies_the_absolute_floor(tmp_path):
    target = tmp_path / aemps.ENTRY_NAME
    shutil.copy(SAMPLE, target)

    with pytest.raises(common.FeedError, match="at least 20000"):
        aemps.validate(target, {})


def test_validate_applies_ninety_percent_of_the_previous_run(tmp_path, monkeypatch):
    monkeypatch.setattr(aemps, "MIN_DATA_ROWS", 100)
    target = tmp_path / aemps.ENTRY_NAME
    shutil.copy(SAMPLE, target)

    with pytest.raises(common.FeedError, match="at least 180"):
        aemps.validate(target, {"aemps.xlsx": 200})


def write_workbook(path, header, rows=()):
    workbook = openpyxl.Workbook()
    sheet = workbook.active
    sheet.append(header)
    for row in rows:
        sheet.append(row)
    workbook.save(path)


def test_the_header_check_ignores_case_and_trailing_empty_cells(tmp_path):
    path = tmp_path / "aemps.xlsx"
    write_workbook(path, [h.upper() for h in aemps.EXPECTED_HEADER] + [None, None], [["1", "X"], [None, None], ["2", "Y"]])

    header, count = aemps.read_workbook(path)
    aemps.check_header(header)
    assert count == 2


@pytest.mark.parametrize(
    "header",
    [
        aemps.EXPECTED_HEADER[:-1],
        aemps.EXPECTED_HEADER + ["Extra"],
        [aemps.EXPECTED_HEADER[1], aemps.EXPECTED_HEADER[0]] + aemps.EXPECTED_HEADER[2:],
    ],
)
def test_the_header_check_rejects_a_different_layout(tmp_path, header):
    path = tmp_path / "aemps.xlsx"
    write_workbook(path, header)

    with pytest.raises(common.FeedError, match="header"):
        aemps.check_header(aemps.read_workbook(path)[0])
