import csv
import datetime

import openpyxl
import pytest

import common
import ema
from conftest import FIXTURES

# ema-epar-sample.xlsx is ema-epar-sample.csv laid out like the EMA
# report: 8 metadata rows above the header, a header row declared wider
# than its 39 columns, one cell with CR/LF, a tab and double spaces, and
# a trailing empty row. ema-epar-from-xlsx.csv is ema.py's conversion of
# it; the .NET parser test reads that file.
SAMPLE_XLSX = FIXTURES / "ema-epar-sample.xlsx"
SAMPLE_CSV = FIXTURES / "ema-epar-sample.csv"
CONVERTED_CSV = FIXTURES / "ema-epar-from-xlsx.csv"


@pytest.fixture
def low_floors(monkeypatch):
    monkeypatch.setattr(ema, "MIN_ROWS", 50)
    monkeypatch.setattr(ema, "MIN_HUMAN_ROWS", 50)


@pytest.mark.parametrize(
    "value,expected",
    [
        (None, ""),
        ("  Human ", "Human"),
        ("line one\r\nline two", "line one line two"),
        ("a\tb\n\nc", "a b c"),
        ("double  spaces", "double spaces"),
        ("keeps nbsp", "keeps nbsp"),
        ("A;B", "A;B"),
        (12.0, "12"),
        (0.5, "0.5"),
        (7, "7"),
        (True, "TRUE"),
        (datetime.datetime(2026, 9, 29, 14, 30), "2026-09-29"),
        (datetime.date(2026, 1, 2), "2026-01-02"),
    ],
)
def test_clean_cell(value, expected):
    assert ema.clean_cell(value) == expected


def test_convert_rows_finds_the_header_and_trims_the_width():
    rows = [
        ("Medicines report", None),
        (None, None),
        ("Category", "Name of medicine", None, None, None),
        ("Human", "A", "overflow"),
        (None, None, None, None),
        ("Veterinary",),
    ]

    header, data = ema.convert_rows(rows)

    assert header == ["Category", "Name of medicine"]
    assert data == [["Human", "A"], ["Veterinary", ""]]


def test_convert_rows_requires_a_header():
    with pytest.raises(common.FeedError, match="Category"):
        ema.convert_rows([("Title",), ("Human", "A")])


def test_read_workbook_prefers_the_Medicine_sheet(tmp_path):
    workbook = openpyxl.Workbook()
    workbook.active.title = "Notes"
    workbook.active.append(["Category", "Wrong"])
    sheet = workbook.create_sheet("Medicine")
    sheet.append(["Category", "Name of medicine"])
    sheet.append(["Human", "Right"])
    path = tmp_path / "report.xlsx"
    workbook.save(path)

    assert ema.read_workbook(path) == (["Category", "Name of medicine"], [["Human", "Right"]])


def normalised(path):
    return path.read_bytes().replace(b"\r\n", b"\n")


def test_the_committed_csv_fixture_is_the_conversion_of_the_xlsx_fixture(tmp_path):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)

    assert target.read_bytes() == normalised(CONVERTED_CSV)


def test_the_conversion_matches_the_manual_export_except_the_multi_line_cell(tmp_path):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)

    converted = target.read_bytes().decode("utf-8").split("\n")
    manual = normalised(SAMPLE_CSV).decode("utf-8-sig").split("\n")
    differing = [i for i, (a, b) in enumerate(zip(converted, manual)) if a != b]

    assert len(converted) == len(manual)
    assert len(differing) == 1
    assert "Second line after a tab and double spaces" in converted[differing[0]]


def test_the_conversion_writes_utf8_without_bom_and_quotes_semicolons(tmp_path):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)
    data = target.read_bytes()

    assert not data.startswith(b"\xef\xbb\xbf")
    assert b"\r" not in data
    assert b'"Immunologicals for felidae;Other immunologicals"' in data


def test_validate_counts_rows_and_human_rows(tmp_path, low_floors):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)

    assert ema.validate(target, {}) == {"ema-epar.csv": 73, "ema-epar.csv (Human)": 70}


def test_validate_applies_the_absolute_floors(tmp_path):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)

    with pytest.raises(common.FeedError, match="at least 2000"):
        ema.validate(target, {})


def test_validate_applies_ninety_percent_of_the_previous_human_count(tmp_path, low_floors):
    target = tmp_path / ema.ENTRY_NAME
    ema.convert(SAMPLE_XLSX, target)

    with pytest.raises(common.FeedError, match=r"\(Human\): 70 rows, at least 72"):
        ema.validate(target, {"ema-epar.csv": 73, "ema-epar.csv (Human)": 80})


def write(path, rows):
    with open(path, "w", encoding="utf-8", newline="") as fh:
        csv.writer(fh, delimiter=";", lineterminator="\n").writerows(rows)


def test_validate_rejects_a_record_split_across_lines(tmp_path, low_floors):
    target = tmp_path / ema.ENTRY_NAME
    header, data = ema.read_workbook(SAMPLE_XLSX)
    data[0][15] = "first line\nsecond line"
    write(target, [header] + data)

    with pytest.raises(common.FeedError, match="line 2 is not a complete record"):
        ema.validate(target, {})


def test_validate_rejects_a_line_of_another_width(tmp_path, low_floors):
    target = tmp_path / ema.ENTRY_NAME
    header, data = ema.read_workbook(SAMPLE_XLSX)
    data[3] = data[3][:-1]
    write(target, [header] + data)

    with pytest.raises(common.FeedError, match="line 5 has 38 columns, 39 expected"):
        ema.validate(target, {})


def test_validate_rejects_a_missing_required_column(tmp_path, low_floors):
    target = tmp_path / ema.ENTRY_NAME
    header, data = ema.read_workbook(SAMPLE_XLSX)
    header[header.index("Medicine URL")] = "URL"
    write(target, [header] + data)

    with pytest.raises(common.FeedError, match="Medicine URL"):
        ema.validate(target, {})


def test_find_report_link_reads_the_landing_page():
    html = '<a href="/en/documents/report/medicines-output-medicines-report_en.xlsx">Medicines</a>'

    assert ema.find_report_link(html) == (
        "https://www.ema.europa.eu/en/documents/report/medicines-output-medicines-report_en.xlsx"
    )
    assert ema.find_report_link("<a href='/other.xlsx'>x</a>") is None


@pytest.mark.parametrize("data", [b"<!DOCTYPE html><html>", b"Category;Name"])
def test_check_payload_rejects_non_xlsx(data):
    with pytest.raises(common.FeedError):
        ema.check_payload(data)


def test_check_payload_accepts_the_fixture():
    ema.check_payload(SAMPLE_XLSX.read_bytes())
