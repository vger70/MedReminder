import json
import zipfile
from datetime import datetime, timezone

import pytest

import common

RUN = datetime(2026, 10, 2, 3, 20, 5, tzinfo=timezone.utc)


def test_version_for_formats_year_and_month():
    assert common.version_for(RUN) == "202610"
    assert common.version_for(datetime(2027, 1, 31, 23, 59, tzinfo=timezone.utc)) == "202701"


def test_run_time_is_utc():
    assert common.run_time().tzinfo == timezone.utc


@pytest.mark.parametrize("value,expected", [("true", True), ("TRUE ", True), ("false", False), ("", False)])
def test_force_refresh_reads_the_environment(monkeypatch, value, expected):
    monkeypatch.setenv("FORCE_REFRESH", value)
    assert common.force_refresh() is expected


def test_force_refresh_defaults_to_false(monkeypatch):
    monkeypatch.delenv("FORCE_REFRESH", raising=False)
    assert common.force_refresh() is False


def test_browser_headers_carry_a_browser_user_agent_and_optional_referer():
    headers = common.browser_headers("https://example.org/")
    assert headers["User-Agent"].startswith("Mozilla/5.0")
    assert headers["Accept"]
    assert headers["Accept-Language"]
    assert headers["Referer"] == "https://example.org/"
    assert "Referer" not in common.browser_headers()


def write_manifest(data_dir, manifest):
    data_dir.mkdir(parents=True, exist_ok=True)
    (data_dir / "latest.json").write_text(json.dumps(manifest), encoding="utf-8")


def test_already_published_compares_the_version(tmp_path):
    write_manifest(tmp_path, {"version": "202610"})
    assert common.already_published(tmp_path, "202610", force=False)
    assert not common.already_published(tmp_path, "202610", force=True)
    assert not common.already_published(tmp_path, "202611", force=False)


def test_already_published_is_false_without_a_manifest(tmp_path):
    assert not common.already_published(tmp_path / "missing", "202610", force=False)
    (tmp_path / "latest.json").write_text("not json", encoding="utf-8")
    assert not common.already_published(tmp_path, "202610", force=False)


def test_previous_rows_reads_the_manifest(tmp_path):
    write_manifest(tmp_path, {"version": "202609", "rows": {"a.csv": 10}})
    assert common.previous_rows(tmp_path) == {"a.csv": 10}


@pytest.mark.parametrize("manifest", [{"version": "202609"}, {"rows": []}, []])
def test_previous_rows_is_empty_for_missing_or_malformed_counts(tmp_path, manifest):
    write_manifest(tmp_path, manifest)
    assert common.previous_rows(tmp_path) == {}


def test_check_rows_applies_the_absolute_floor():
    assert common.check_rows("x", 100, 100) == 100
    with pytest.raises(common.FeedError, match="99 rows, at least 100"):
        common.check_rows("x", 99, 100)


def test_check_rows_applies_ninety_percent_of_the_previous_count():
    assert common.check_rows("x", 900, 100, previous=1000) == 900
    with pytest.raises(common.FeedError, match="at least 900"):
        common.check_rows("x", 899, 100, previous=1000)


@pytest.mark.parametrize("previous", [None, 0, -5, "1000", True])
def test_check_rows_ignores_unusable_previous_counts(previous):
    assert common.check_rows("x", 150, 100, previous=previous) == 150


@pytest.mark.parametrize(
    "data",
    [
        b"<!DOCTYPE html><html><body>403</body></html>",
        b"\xef\xbb\xbf  <html lang='fr'>",
        b"<head><title>Error</title></head>",
        b"\r\n<!-- proxy -->\n<html>",
    ],
)
def test_looks_like_html_detects_pages(data):
    assert common.looks_like_html(data)


@pytest.mark.parametrize(
    "data",
    [b"PK\x03\x04\x14\x00", b"Category;Name of medicine\n", b"60002283\tANASTROZOLE\t", b""],
)
def test_looks_like_html_accepts_data(data):
    assert not common.looks_like_html(data)


def test_write_zip_stores_entries_under_their_archive_names(tmp_path):
    source = tmp_path / "Medicamentos.xls"
    source.write_bytes(b"payload")
    archive = common.write_zip(tmp_path / "aemps-202610.zip", {"aemps.xlsx": source})

    with zipfile.ZipFile(archive) as zf:
        assert zf.namelist() == ["aemps.xlsx"]
        assert zf.read("aemps.xlsx") == b"payload"
        assert zf.getinfo("aemps.xlsx").compress_type == zipfile.ZIP_DEFLATED


def build_zip(directory, name):
    source = directory / "entry.txt"
    source.write_text(name, encoding="utf-8")
    return common.write_zip(directory / name, {"entry.txt": source})


def test_publish_moves_the_archive_and_writes_the_manifest(tmp_path):
    work = tmp_path / "work"
    work.mkdir()
    data_dir = tmp_path / "data" / "es"
    archive = build_zip(work, "aemps-202610.zip")
    expected_sha = common.sha256_of(archive)
    expected_size = archive.stat().st_size

    manifest = common.publish(data_dir, "ES", "202610", archive, 1, {"aemps.xlsx": 26763}, RUN)

    assert not archive.exists()
    assert (data_dir / "aemps-202610.zip").exists()
    on_disk = json.loads((data_dir / "latest.json").read_text(encoding="utf-8"))
    assert on_disk == manifest
    assert manifest == {
        "country": "ES",
        "version": "202610",
        "file": "aemps-202610.zip",
        "generated": "2026-10-02T03:20:05+00:00",
        "file_count": 1,
        "sha256": expected_sha,
        "size": expected_size,
        "rows": {"aemps.xlsx": 26763},
    }


def test_publish_keeps_the_three_newest_archives_of_the_prefix(tmp_path):
    work = tmp_path / "work"
    work.mkdir()
    data_dir = tmp_path / "data" / "eu"
    data_dir.mkdir(parents=True)
    for version in ("202606", "202607", "202608", "202609"):
        (data_dir / f"ema-epar-{version}.zip").write_bytes(b"old")
    (data_dir / "other-202601.zip").write_bytes(b"unrelated")

    common.publish(data_dir, "EU", "202610", build_zip(work, "ema-epar-202610.zip"), 1, {}, RUN)

    assert sorted(p.name for p in data_dir.glob("*.zip")) == [
        "ema-epar-202608.zip",
        "ema-epar-202609.zip",
        "ema-epar-202610.zip",
        "other-202601.zip",
    ]


def test_publish_replaces_a_forced_rebuild_of_the_same_month(tmp_path):
    work = tmp_path / "work"
    work.mkdir()
    data_dir = tmp_path / "data" / "fr"
    data_dir.mkdir(parents=True)
    (data_dir / "bdpm-202610.zip").write_bytes(b"first build")

    manifest = common.publish(data_dir, "FR", "202610", build_zip(work, "bdpm-202610.zip"), 3, {}, RUN)

    assert common.sha256_of(data_dir / "bdpm-202610.zip") == manifest["sha256"]


def test_publish_refuses_an_archive_named_for_another_version(tmp_path):
    archive = build_zip(tmp_path, "bdpm-202609.zip")
    with pytest.raises(common.FeedError):
        common.publish(tmp_path / "data", "FR", "202610", archive, 3, {}, RUN)
    assert not (tmp_path / "data" / "latest.json").exists()
