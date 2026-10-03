import json

import pytest

import aifa_equivalents
import common
from conftest import FIXTURES

SAMPLE = FIXTURES / "aifa-equivalents-sample.csv"


def sample_text():
    return aifa_equivalents.decode(SAMPLE.read_bytes())


def test_the_sample_is_windows_1252_and_decodes():
    raw = SAMPLE.read_bytes()
    with pytest.raises(UnicodeDecodeError):
        raw.decode("utf-8")
    assert "€" in aifa_equivalents.decode(raw)


def test_parse_reads_the_list_date_from_the_price_column():
    list_date, groups = aifa_equivalents.parse(sample_text())

    assert list_date.isoformat() == "2026-09-15"
    assert [g["code"] for g in groups] == ["12A", "341", "810"]
    assert aifa_equivalents.package_count(groups) == 8


def test_parse_pads_the_aic_and_converts_prices_to_cents():
    _, groups = aifa_equivalents.parse(sample_text())
    amlodipine = {g["code"]: g for g in groups}["341"]

    assert amlodipine["ingredient"] == "AMLODIPINA"
    assert amlodipine["reference"] == "28 UNITA' 5 MG - USO ORALE"
    assert amlodipine["atc"] == "C08CA01"
    assert amlodipine["referencePrice"] == 295
    norvasc = next(m for m in amlodipine["members"] if m["name"] == "NORVASC")
    assert norvasc == {
        "aic": "002783013", "name": "NORVASC", "package": "5 MG COMPRESSE 28 COMPRESSE",
        "holder": "PFIZER ITALIA S.R.L.", "price": 549, "difference": 254, "note": None,
    }


def test_parse_keeps_each_package_note_verbatim():
    _, groups = aifa_equivalents.parse(sample_text())
    nifedipine = {g["code"]: g for g in groups}["12A"]
    notes = {m["name"]: m["note"] for m in nifedipine["members"]}

    assert notes == {
        "ADALAT CRONO": None,
        "NIFEDIPINA DOC*": "*non sostituibile con Adalat Crono",
        "NIFEDIPINA EG": None,
    }
    salbutamol = {g["code"]: g for g in groups}["810"]
    assert salbutamol["reference"] == "200 DOSI 100 MCG - USO INALATORIO"


@pytest.mark.parametrize("text,cents", [
    ("5,63 €", 563), ("0,00 €", 0), ("12 €", 1200), ("1.234,5 €", 123450), ("-0,40 €", -40), ("", None),
])
def test_price_cents(text, cents):
    assert aifa_equivalents.price_cents(text, "Prezzo", "000000000") == cents


def test_a_malformed_price_or_code_is_rejected():
    with pytest.raises(common.FeedError, match="not a price"):
        aifa_equivalents.price_cents("n.d.", "Prezzo", "000000000")
    with pytest.raises(common.FeedError, match="up to 9 digits"):
        aifa_equivalents.parse(sample_text().replace("035123037", "0351230370"))


def test_parse_rejects_a_package_listed_in_two_groups():
    text = sample_text().replace("037002033;AMLODIPINA MYLAN", "026622050;AMLODIPINA MYLAN")
    with pytest.raises(common.FeedError, match="groups 12A and 341"):
        aifa_equivalents.parse(text)


def test_parse_rejects_a_file_without_the_header_or_the_date():
    with pytest.raises(common.FeedError, match="header"):
        aifa_equivalents.parse("nothing here")
    with pytest.raises(common.FeedError, match="list date"):
        aifa_equivalents.parse(sample_text().replace("15 settembre 2026", "corrente"))
    with pytest.raises(common.FeedError, match="missing columns"):
        aifa_equivalents.parse(sample_text().replace("Prezzo Pubblico", "Prezzo al pubblico"))


def test_check_count_applies_the_floors():
    aifa_equivalents.check_count(4000, None)
    with pytest.raises(common.FeedError, match="at least 4000"):
        aifa_equivalents.check_count(3999, None)
    with pytest.raises(common.FeedError, match="at least 9000"):
        aifa_equivalents.check_count(8999, 10000)


def test_publish_writes_the_file_and_a_matching_manifest(tmp_path):
    list_date, groups = aifa_equivalents.parse(sample_text())
    generated = common.run_time()
    document = aifa_equivalents.build_document(list_date, groups, generated)

    manifest = aifa_equivalents.publish(tmp_path, list_date, document, generated)

    published = tmp_path / "equivalents-20260915.json"
    assert manifest["file"] == published.name
    assert manifest["version"] == "20260915"
    assert manifest["size"] == published.stat().st_size
    assert manifest["sha256"] == common.sha256_of(published)
    assert manifest["rows"] == {"groups": 3, "packages": 8}
    assert json.loads((tmp_path / "latest.json").read_text(encoding="utf-8")) == manifest
    body = json.loads(published.read_text(encoding="utf-8"))
    assert body["listDate"] == "2026-09-15" and body["country"] == "IT" and len(body["groups"]) == 3


def test_publish_keeps_the_three_newest_files(tmp_path):
    for day in ("20260615", "20260715", "20260815", "20260901"):
        (tmp_path / f"equivalents-{day}.json").write_text("{}", encoding="utf-8")
    list_date, groups = aifa_equivalents.parse(sample_text())
    generated = common.run_time()

    aifa_equivalents.publish(tmp_path, list_date, aifa_equivalents.build_document(list_date, groups, generated), generated)

    assert sorted(p.name for p in tmp_path.glob("equivalents-*.json")) == [
        "equivalents-20260815.json", "equivalents-20260901.json", "equivalents-20260915.json",
    ]
