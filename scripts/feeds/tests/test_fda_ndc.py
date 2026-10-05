import io
import json
import zipfile
from datetime import date

import pytest

import common
import fda_ndc
from conftest import FIXTURES

SAMPLE_JSON = FIXTURES / "openfda-ndc-sample.json"
# What the script writes for SAMPLE_JSON on RUN_DATE; OpenFdaNdcParser's
# tests read the same file, so both sides agree on the layout.
SAMPLE_TSV = FIXTURES / "fda-ndc-sample.tsv"
RUN_DATE = date(2026, 10, 5)


@pytest.fixture
def products():
    return json.loads(SAMPLE_JSON.read_text(encoding="utf-8"))["results"]


@pytest.fixture
def low_floor(monkeypatch):
    monkeypatch.setattr(fda_ndc, "MIN_ROWS", 5)


def zipped(document, name="drug-ndc-0001-of-0001.json"):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as zf:
        zf.writestr(name, json.dumps(document))
    return buffer.getvalue()


@pytest.mark.parametrize(
    "published, expected",
    [
        ("0002-1001-30", "000002-1001-30"),
        ("51234-200-10", "051234-0200-10"),
        ("60001-1234-1", "060001-1234-01"),
        ("123456-1234-12", "123456-1234-12"),
        (" 0002-1001-30 ", "000002-1001-30"),
        ("80000111-01", None),
        ("0002-100-30", None),
        ("ABCD-1001-30", None),
        ("", None),
        (None, None),
    ],
)
def test_canonical_ndc_pads_each_segment_to_6_4_2(published, expected):
    assert fda_ndc.canonical_ndc(published) == expected


def test_the_sample_converts_to_the_shared_fixture(products, tmp_path):
    rows, skipped = fda_ndc.rows_from_products(products, RUN_DATE)
    path = tmp_path / fda_ndc.ENTRY_NAME
    fda_ndc.write_tsv(path, rows)

    assert path.read_bytes() == SAMPLE_TSV.read_bytes()
    assert skipped == 2


def test_only_finished_prescription_otc_and_vaccine_products_are_kept(products):
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)
    names = {row[2] for row in rows}

    assert "Fluexample" in names
    assert "Pollen Example" not in names
    assert not any(row[1].startswith("12345-") for row in rows)


def test_sample_packages_are_dropped(products):
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)

    assert "0002-1001-01" not in {row[1] for row in rows}


def test_a_past_end_date_marks_the_package_discontinued(products):
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)
    status = {row[1]: row[7] for row in rows}

    assert status["51234-200-24"] == "Discontinued (OTC MONOGRAPH DRUG, marketing ended 2024-06-30)"
    assert status["60001-1234-1"] == "Discontinued (ANDA, marketing ended 2025-12-31)"
    assert status["0409-4888-02"] == "NDA"
    assert status["51234-200-10"] == "OTC MONOGRAPH DRUG"


def test_the_regime_carries_the_dea_schedule(products):
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)
    regime = {row[1]: row[8] for row in rows}

    assert regime["0409-4888-02"] == "Rx, DEA CII"
    assert regime["51234-200-10"] == "OTC"


def test_only_a_uuid_set_id_is_kept(products):
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)
    set_ids = {row[1]: row[10] for row in rows}

    assert set_ids["0002-1001-30"] == "c0ffee00-1111-4222-8333-444455556666"
    assert set_ids["60001-1234-1"] == ""


def test_two_codes_with_the_same_12_digit_form_fail_the_run(products):
    products.append({
        "product_type": "HUMAN OTC DRUG",
        "brand_name": "Clash",
        "packaging": [{"package_ndc": "000002-1001-30"}],
    })
    with pytest.raises(common.FeedError, match="same 12-digit NDC"):
        fda_ndc.rows_from_products(products, RUN_DATE)


def test_tabs_and_line_breaks_inside_values_become_spaces(products):
    products[0]["brand_name"] = "Exam\tplor\nXR"
    rows, _ = fda_ndc.rows_from_products(products, RUN_DATE)

    assert rows[0][2] == "Exam plor XR"


def test_validate_counts_the_rows(low_floor):
    assert fda_ndc.validate(SAMPLE_TSV, {}) == {fda_ndc.ENTRY_NAME: 8}


def test_validate_applies_the_absolute_floor():
    with pytest.raises(common.FeedError, match="at least 20000"):
        fda_ndc.validate(SAMPLE_TSV, {})


def test_validate_applies_ninety_percent_of_the_previous_run(low_floor):
    with pytest.raises(common.FeedError, match="8 rows, at least 9"):
        fda_ndc.validate(SAMPLE_TSV, {fda_ndc.ENTRY_NAME: 10})


def test_validate_rejects_a_line_with_another_column_count(tmp_path, low_floor):
    path = tmp_path / fda_ndc.ENTRY_NAME
    path.write_text(SAMPLE_TSV.read_text(encoding="utf-8") + "000001-0001-01\tbroken\n", encoding="utf-8")
    with pytest.raises(common.FeedError, match="2 columns, 11 expected"):
        fda_ndc.validate(path, {})


def test_validate_rejects_another_header(tmp_path, low_floor):
    path = tmp_path / fda_ndc.ENTRY_NAME
    path.write_text("ndc\tname\n", encoding="utf-8")
    with pytest.raises(common.FeedError, match="unexpected header"):
        fda_ndc.validate(path, {})


def test_partition_urls_reads_the_manifest():
    manifest = {"results": {"drug": {"ndc": {"partitions": [
        {"file": "https://download.open.fda.gov/drug/ndc/drug-ndc-0001-of-0001.json.zip", "records": 1},
        {"file": "https://elsewhere.example.org/drug-ndc.json.zip"},
        {"file": "http://download.open.fda.gov/drug/ndc/plain-http.json.zip"},
        "junk",
    ]}}}}

    assert fda_ndc.partition_urls(manifest) == [
        "https://download.open.fda.gov/drug/ndc/drug-ndc-0001-of-0001.json.zip"
    ]


@pytest.mark.parametrize("manifest", [{}, {"results": {"drug": {}}}, [], None, {"results": {"drug": {"ndc": {"partitions": "x"}}}}])
def test_partition_urls_is_empty_for_an_unexpected_manifest(manifest):
    assert fda_ndc.partition_urls(manifest) == []


def test_products_of_reads_the_results_of_the_export(products):
    assert fda_ndc.products_of(zipped({"meta": {}, "results": products})) == products


def test_products_of_rejects_an_html_page():
    with pytest.raises(common.FeedError, match="HTML"):
        fda_ndc.products_of(b"<!DOCTYPE html><html><body>Error</body></html>")


def test_products_of_rejects_an_export_without_results():
    with pytest.raises(common.FeedError, match="'results'"):
        fda_ndc.products_of(zipped({"meta": {}}))


def test_products_of_rejects_a_non_zip_payload():
    with pytest.raises(common.FeedError, match="not a ZIP"):
        fda_ndc.products_of(b'{"results": []}')
