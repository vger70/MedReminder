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


def test_publish_never_deletes_the_file_the_manifest_names(tmp_path):
    for day in ("20261001", "20261002", "20261003"):
        (tmp_path / f"shortages-{day}.json").write_text("{}", encoding="utf-8")
    list_date, entries = aifa_shortages.parse(sample_text())
    generated = common.run_time()

    manifest = aifa_shortages.publish(
        tmp_path, list_date, aifa_shortages.build_document(list_date, entries, generated), generated)

    assert (tmp_path / manifest["file"]).exists()
    assert sorted(p.name for p in tmp_path.glob("shortages-*.json")) == [
        "shortages-20260929.json", "shortages-20261002.json", "shortages-20261003.json",
    ]


class FakeResponse:
    def __init__(self, status, content=b"", headers=None):
        self.status_code = status
        self.content = content
        self.headers = headers or {}

    def raise_for_status(self):
        if self.status_code >= 400:
            raise RuntimeError(self.status_code)


class FakeSession:
    def __init__(self, response):
        self.response = response
        self.headers = None

    def get(self, url, headers=None, timeout=None):
        self.headers = headers
        return self.response


def run_main(monkeypatch, tmp_path, response, force=False):
    monkeypatch.setattr(aifa_shortages, "DATA_DIR", tmp_path)
    # The sample holds 6 entries, below the production floor.
    monkeypatch.setattr(aifa_shortages, "MIN_ENTRIES", 1)
    session = FakeSession(response)
    monkeypatch.setattr(common, "session", lambda: session)
    monkeypatch.setenv("FORCE_REFRESH", "true" if force else "false")
    assert aifa_shortages.main([]) == 0
    return session


def sample_response(etag='"v1"', text=None):
    raw = SAMPLE.read_bytes() if text is None else text.encode("cp1252")
    return FakeResponse(200, raw, {"ETag": etag, "Last-Modified": "Tue, 29 Sep 2026 10:00:00 GMT"})


def manifest_of(tmp_path):
    return json.loads((tmp_path / "latest.json").read_text(encoding="utf-8"))


def test_main_publishes_and_records_the_source_validators(monkeypatch, tmp_path):
    run_main(monkeypatch, tmp_path, sample_response())

    manifest = manifest_of(tmp_path)
    assert manifest["version"] == "20260929"
    assert manifest["source"] == {"etag": '"v1"', "lastModified": "Tue, 29 Sep 2026 10:00:00 GMT"}


def test_main_sends_the_validators_and_stops_on_304(monkeypatch, tmp_path, capsys):
    run_main(monkeypatch, tmp_path, sample_response())
    before = (tmp_path / "latest.json").read_bytes()

    session = run_main(monkeypatch, tmp_path, FakeResponse(304))

    assert session.headers["If-None-Match"] == '"v1"'
    assert session.headers["If-Modified-Since"] == "Tue, 29 Sep 2026 10:00:00 GMT"
    assert "unchanged" in capsys.readouterr().out
    assert (tmp_path / "latest.json").read_bytes() == before


def test_main_forced_ignores_the_validators(monkeypatch, tmp_path):
    run_main(monkeypatch, tmp_path, sample_response())

    session = run_main(monkeypatch, tmp_path, sample_response(), force=True)

    assert "If-None-Match" not in session.headers


def test_main_skips_the_same_list_and_records_new_validators(monkeypatch, tmp_path):
    run_main(monkeypatch, tmp_path, sample_response())
    published = manifest_of(tmp_path)

    run_main(monkeypatch, tmp_path, sample_response(etag='"v2"'))

    manifest = manifest_of(tmp_path)
    assert manifest["sha256"] == published["sha256"]
    assert manifest["generated"] == published["generated"]
    assert manifest["source"]["etag"] == '"v2"'


def test_main_republishes_a_correction_with_the_same_date(monkeypatch, tmp_path):
    run_main(monkeypatch, tmp_path, sample_response())
    published = manifest_of(tmp_path)

    corrected = sample_text().replace("087654321", "087654329")
    run_main(monkeypatch, tmp_path, sample_response(etag='"v2"', text=corrected))

    manifest = manifest_of(tmp_path)
    assert manifest["version"] == "20260929"
    assert manifest["sha256"] != published["sha256"]
    body = json.loads((tmp_path / manifest["file"]).read_text(encoding="utf-8"))
    assert "087654329" in [e["aic"] for e in body["entries"]]


@pytest.mark.parametrize("force", [False, True])
def test_main_refuses_an_older_list_than_the_published_one(monkeypatch, tmp_path, capsys, force):
    newer = sample_text().replace("aggiornato al 29/09/2026", "aggiornato al 03/10/2026")
    assert newer != sample_text()
    run_main(monkeypatch, tmp_path, sample_response(text=newer))
    published = (tmp_path / "latest.json").read_bytes()

    run_main(monkeypatch, tmp_path, sample_response(etag='"stale"'), force=force)

    assert "refusing to roll back" in capsys.readouterr().out
    assert manifest_of(tmp_path)["version"] == "20261003"
    assert (tmp_path / "shortages-20261003.json").exists()
    assert not (tmp_path / "shortages-20260929.json").exists()
    assert manifest_of(tmp_path)["sha256"] == json.loads(published)["sha256"]
