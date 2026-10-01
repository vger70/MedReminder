import json

import pytest

import aifa_shortages
import common
from conftest import FIXTURES

SAMPLE = FIXTURES / "aifa-shortages-sample.csv"


def sample_text():
    return aifa_shortages.decode(SAMPLE.read_bytes())


def test_the_sample_is_windows_1252_and_decodes():
    raw = SAMPLE.read_bytes()
    with pytest.raises(UnicodeDecodeError):
        raw.decode("utf-8")
    assert "Sì" in aifa_shortages.decode(raw)


def test_parse_skips_the_preamble_and_reads_the_list_date():
    list_date, entries = aifa_shortages.parse(sample_text())

    assert list_date.isoformat() == "2026-09-29"
    assert [e["aic"] for e in entries] == [
        "012345678", "037839115", "039073022", "043857022", "045348036", "087654321",
    ]


def test_parse_keeps_fields_with_line_breaks_and_drops_duplicates():
    _, entries = aifa_shortages.parse(sample_text())
    by_aic = {e["aic"]: e for e in entries}

    assert by_aic["037839115"] == {
        "aic": "037839115", "start": "2026-08-10", "expectedEnd": "2026-10-31",
        "equivalent": True, "reason": "production",
    }
    assert by_aic["039073022"]["reason"] == "production"
    assert by_aic["043857022"]["expectedEnd"] is None
    assert by_aic["045348036"]["equivalent"] is True
    assert by_aic["012345678"] == {
        "aic": "012345678", "start": "2028-05-01", "expectedEnd": None,
        "equivalent": True, "reason": "withdrawn",
    }
    assert by_aic["087654321"]["reason"] == "other"


@pytest.mark.parametrize("text,category", [
    ("Problemi produttivi", "production"),
    ("Elevata richiesta", "demand"),
    ("Elevata richiesta/problemi produttivi: forniture discontinue", "production"),
    ("Cessata commercializzazione definitiva", "withdrawn"),
    ("Cessata commercializzazione temporanea", "suspended"),
    ("Motivi commerciali", "commercial"),
    ("Problemi regolatori", "regulatory"),
    ("", "other"),
])
def test_reason_categories(text, category):
    assert aifa_shortages.reason_category(text) == category


def test_parse_rejects_a_file_without_the_header_or_the_date():
    with pytest.raises(common.FeedError, match="header"):
        aifa_shortages.parse("nothing here")
    with pytest.raises(common.FeedError, match="list date"):
        aifa_shortages.parse(sample_text().replace("aggiornato al 29/09/2026", "senza data"))


def test_parse_rejects_a_malformed_code_or_date():
    with pytest.raises(common.FeedError, match="9 digits"):
        aifa_shortages.parse(sample_text().replace("045348036", "45348036"))
    with pytest.raises(common.FeedError, match="dd/mm/yyyy"):
        aifa_shortages.parse(sample_text().replace("28/07/2025", "2025-07-28"))


def test_check_count_applies_the_floors():
    aifa_shortages.check_count(300, None)
    with pytest.raises(common.FeedError, match="at least 300"):
        aifa_shortages.check_count(299, None)
    with pytest.raises(common.FeedError, match="at least 1000"):
        aifa_shortages.check_count(999, 2000)


def test_publish_writes_the_file_and_a_matching_manifest(tmp_path):
    list_date, entries = aifa_shortages.parse(sample_text())
    generated = common.run_time()
    document = aifa_shortages.build_document(list_date, entries, generated)

    manifest = aifa_shortages.publish(tmp_path, list_date, document, generated)

    published = tmp_path / "shortages-20260929.json"
    assert manifest["file"] == published.name
    assert manifest["version"] == "20260929"
    assert manifest["size"] == published.stat().st_size
    assert manifest["sha256"] == common.sha256_of(published)
    assert manifest["rows"] == {"entries": 6}
    assert json.loads((tmp_path / "latest.json").read_text(encoding="utf-8")) == manifest
    body = json.loads(published.read_text(encoding="utf-8"))
    assert body["listDate"] == "2026-09-29" and body["country"] == "IT" and len(body["entries"]) == 6


def test_publish_keeps_the_three_newest_files(tmp_path):
    for day in ("20260901", "20260908", "20260915", "20260922"):
        (tmp_path / f"shortages-{day}.json").write_text("{}", encoding="utf-8")
    list_date, entries = aifa_shortages.parse(sample_text())
    generated = common.run_time()

    aifa_shortages.publish(tmp_path, list_date, aifa_shortages.build_document(list_date, entries, generated), generated)

    assert sorted(p.name for p in tmp_path.glob("shortages-*.json")) == [
        "shortages-20260915.json", "shortages-20260922.json", "shortages-20260929.json",
    ]
