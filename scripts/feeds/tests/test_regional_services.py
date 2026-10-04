import copy
import hashlib
import json
from datetime import date, datetime, timezone

import pytest

import common
import regional_services

TODAY = date(2026, 10, 4)
RUN = datetime(2026, 10, 4, 5, 12, 0, tzinfo=timezone.utc)

SAMPLE = {
    "country": "IT",
    "listDate": "2026-10-04",
    "services": [
        {
            "regionCode": "12",
            "region": "Lazio",
            "service": "Salute Lazio",
            "webUrl": "https://www.salutelazio.it/",
            "iosAppUrl": "https://apps.apple.com/it/app/salutelazio/id1201847471",
            "androidAppUrl": None,
            "signIn": ["SPID", "CIE", "TS-CNS"],
            "showsPrescriptions": True,
            "familyDelegation": True,
            "verifiedOn": "2026-10-04",
        },
        {
            "regionCode": "05",
            "region": "Veneto",
            "service": "Sanità km zero Ricette",
            "webUrl": "https://www.sanitakmzero.it/",
            "signIn": ["SPID"],
            "showsPrescriptions": False,
            "familyDelegation": False,
            "verifiedOn": "2026-09-30",
        },
    ],
}


def sample():
    return copy.deepcopy(SAMPLE)


def test_the_sample_is_valid_and_an_entry_may_have_a_web_url_only():
    services = regional_services.validate(sample(), TODAY)
    assert [s["regionCode"] for s in services] == ["12", "05"]


def test_the_shipped_source_is_valid():
    source = regional_services.load_source()
    services = regional_services.validate(source, date.fromisoformat(source["listDate"]))
    codes = [s["regionCode"] for s in services]
    assert len(codes) == len(set(codes))
    assert set(codes) <= regional_services.REGION_CODES


def test_there_are_21_region_codes():
    assert len(regional_services.REGION_CODES) == 21
    assert "04" not in regional_services.REGION_CODES


def mutate(change):
    document = sample()
    change(document)
    return document


@pytest.mark.parametrize("change,message", [
    (lambda d: d["services"][0].pop("regionCode"), "no 'regionCode'"),
    (lambda d: d["services"][1].update(regionCode="12"), "appears twice"),
    (lambda d: d["services"][0].update(regionCode="04"), "unknown 'regionCode'"),
    (lambda d: d["services"][0].update(webUrl="http://www.salutelazio.it/"), "not an https URL"),
    (lambda d: d["services"][0].update(iosAppUrl="ftp://example.org/app"), "not an https URL"),
    (lambda d: d["services"][0].update(webUrl="not a url"), "not an https URL"),
    (lambda d: d["services"][0].update(webUrl="https://user:pw@example.org/"), "credentials"),
    (lambda d: d["services"][0].update(webUrl="https://example.org:8443/"), "a port"),
    (lambda d: d["services"][0].update(webUrl="https://example.org:port/"), "not an https URL"),
    (lambda d: d["services"][0].update(signIn=["SPID", "Password"]), "unknown 'signIn'"),
    (lambda d: d["services"][0].update(signIn=[]), "no 'signIn'"),
    (lambda d: d["services"][0].update(verifiedOn="2026-10-05"), "in the future"),
    (lambda d: d["services"][0].update(verifiedOn="04/10/2026"), "not a yyyy-mm-dd date"),
    (lambda d: d["services"][0].update(showsPrescriptions="yes"), "boolean 'showsPrescriptions'"),
    (lambda d: d["services"][0].pop("service"), "no valid 'service'"),
    (lambda d: d.update(country="FR"), "not for IT"),
    (lambda d: d.update(listDate="2026-10-05"), "in the future"),
    (lambda d: d.pop("services"), "no 'services'"),
])
def test_validate_refuses_each_invalid_case(change, message):
    with pytest.raises(regional_services.ListError, match=message):
        regional_services.validate(mutate(change), TODAY)


def test_publish_writes_the_list_and_a_manifest_with_size_and_sha256(tmp_path):
    document = regional_services.build_document(sample(), RUN.date(), RUN)

    manifest = regional_services.publish(tmp_path, RUN.date(), document, RUN)

    written = tmp_path / "regional-services-20261004.json"
    payload = written.read_bytes()
    assert manifest["file"] == written.name
    assert manifest["version"] == "20261004"
    assert manifest["country"] == "IT"
    assert manifest["size"] == len(payload)
    assert manifest["sha256"] == hashlib.sha256(payload).hexdigest()
    assert manifest["rows"] == {"services": 2}
    assert json.loads((tmp_path / "latest.json").read_text(encoding="utf-8")) == manifest
    published = json.loads(payload)
    assert published["listDate"] == "2026-10-04"
    assert published["generated"] == RUN.isoformat()
    assert published["services"] == SAMPLE["services"]


def test_an_unchanged_source_is_not_published_again_on_a_later_day(tmp_path):
    first = regional_services.build_document(sample(), RUN.date(), RUN)
    regional_services.publish(tmp_path, RUN.date(), first, RUN)
    later = datetime(2026, 11, 2, tzinfo=timezone.utc)

    again = regional_services.build_document(sample(), later.date(), later)

    assert regional_services.skip_reason(tmp_path, again, force=False) == "the list is already published"
    assert regional_services.skip_reason(tmp_path, again, force=True) is None


def test_a_changed_source_is_published(tmp_path):
    first = regional_services.build_document(sample(), RUN.date(), RUN)
    regional_services.publish(tmp_path, RUN.date(), first, RUN)
    changed = sample()
    changed["services"][1]["showsPrescriptions"] = True

    document = regional_services.build_document(changed, RUN.date(), RUN)

    assert regional_services.skip_reason(tmp_path, document, force=False) is None


def test_nothing_published_yet_publishes(tmp_path):
    document = regional_services.build_document(sample(), RUN.date(), RUN)
    assert regional_services.skip_reason(tmp_path / "missing", document, force=False) is None


class FakeResponse:
    def __init__(self, status):
        self.status_code = status

    def close(self):
        pass


class FakeSession:
    def __init__(self, head, get=None):
        self._head = head
        self._get = get or {}
        self.gets = []

    def head(self, url, **_):
        result = self._head[url]
        if isinstance(result, Exception):
            raise result
        return FakeResponse(result)

    def get(self, url, **_):
        self.gets.append(url)
        return FakeResponse(self._get[url])


def test_check_urls_reports_failures_and_retries_a_refused_head_with_get():
    services = sample()["services"]
    session = FakeSession(
        head={
            "https://www.salutelazio.it/": 405,
            "https://apps.apple.com/it/app/salutelazio/id1201847471": 200,
            "https://www.sanitakmzero.it/": ConnectionError("down"),
        },
        get={"https://www.salutelazio.it/": 404},
    )

    failures = regional_services.check_urls(services, session)

    assert session.gets == ["https://www.salutelazio.it/"]
    assert failures == [
        ("12", "webUrl", "https://www.salutelazio.it/", "HTTP 404"),
        ("05", "webUrl", "https://www.sanitakmzero.it/", "ConnectionError"),
    ]
    text = regional_services.report(failures, "2026-10-04")
    assert "| 12 | `webUrl` | https://www.salutelazio.it/ | HTTP 404 |" in text
    assert regional_services.report([], "2026-10-04") == ""


def test_main_validate_only_does_not_publish(tmp_path, monkeypatch):
    source = tmp_path / "source.json"
    source.write_text(json.dumps(sample()), encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(common, "run_time", lambda: RUN)

    assert regional_services.main(["--validate-only", "--source", str(source)]) == 0
    assert not (tmp_path / "data").exists()


def test_main_publishes_under_data_it_regional_services(tmp_path, monkeypatch):
    source = tmp_path / "source.json"
    source.write_text(json.dumps(sample()), encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(common, "run_time", lambda: RUN)
    monkeypatch.delenv("FORCE_REFRESH", raising=False)

    assert regional_services.main(["--source", str(source)]) == 0
    assert (tmp_path / "data/it/regional-services/regional-services-20261004.json").exists()
    assert (tmp_path / "data/it/regional-services/latest.json").exists()
