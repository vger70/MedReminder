"""Shared helpers for the catalogue feed scripts.

Each feed script downloads one national (or EU) medicines list, checks
it, zips it in the layout the app's parser reads and publishes the
archive plus a `latest.json` manifest under data/<country>/
(docs/analysis/ANALYSIS-CATALOGUE-REMOTE-FEEDS-EU-ES-FR.md §4). Nothing
under data/ changes until every check has passed.
"""

import hashlib
import json
import os
import shutil
import zipfile
from datetime import datetime, timezone
from pathlib import Path

import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry

# Archives kept per feed; older ones are deleted when a new one is
# published. Clients only read the newest, the others allow a manual
# rollback.
RETAINED_ARCHIVES = 3

# A file may not shrink below this share of the previous published run.
MIN_SHARE_OF_PREVIOUS = 0.9

BROWSER_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/130.0 Safari/537.36"
)


class FeedError(RuntimeError):
    """A check failed; nothing is published."""


def session():
    """A requests session that retries transient errors.

    Up to 5 retries on connection errors, 429 and 5xx, with exponential
    back-off (urllib3 waits 0, 30, 60, 120 and 120 s: about 5.5 minutes
    in total), honouring Retry-After. A persistent error still fails the
    run; the next scheduled run tries again.
    """
    retry = Retry(
        total=5,
        connect=5,
        read=5,
        status=5,
        backoff_factor=15,
        backoff_max=120,
        status_forcelist=(429, 500, 502, 503, 504),
        allowed_methods=frozenset({"GET"}),
        respect_retry_after_header=True,
        raise_on_status=False,
    )
    s = requests.Session()
    s.mount("https://", HTTPAdapter(max_retries=retry))
    s.mount("http://", HTTPAdapter(max_retries=retry))
    return s


def browser_headers(referer=None):
    """Headers of a desktop browser; some sources answer 403 without them."""
    headers = {
        "User-Agent": BROWSER_USER_AGENT,
        "Accept": (
            "text/html,application/xhtml+xml,application/xml;q=0.9,"
            "application/vnd.ms-excel,application/vnd.openxmlformats-officedocument."
            "spreadsheetml.sheet,*/*;q=0.8"
        ),
        "Accept-Language": "en-GB,en;q=0.9",
    }
    if referer:
        headers["Referer"] = referer
    return headers


def run_time():
    """One UTC timestamp for the whole run.

    The archive name, the manifest version and `generated` must agree
    even when a run crosses a month boundary, otherwise every client
    rejects the manifest.
    """
    return datetime.now(timezone.utc)


def version_for(ts):
    return f"{ts:%Y%m}"


def force_refresh():
    """FORCE_REFRESH=true (workflow_dispatch input) rebuilds the month."""
    return os.getenv("FORCE_REFRESH", "").strip().lower() == "true"


def read_manifest(data_dir):
    try:
        with open(Path(data_dir) / "latest.json", encoding="utf-8") as fh:
            manifest = json.load(fh)
    except (OSError, ValueError):
        return {}
    return manifest if isinstance(manifest, dict) else {}


def already_published(data_dir, version, force):
    """True when `version` is published and no rebuild was requested.

    Workflows run several times a month so that an outage is retried a
    week later; once the month is published the remaining runs stop.
    A forced rebuild is re-imported by clients because its new
    `generated` and `sha256` make it a later build
    (ANALYSIS-CATALOGUE-REMOTE-FEED.md §11.2).
    """
    return not force and read_manifest(data_dir).get("version") == version


def previous_rows(data_dir):
    """Row counts recorded by the previous published run, by file name."""
    rows = read_manifest(data_dir).get("rows", {})
    return rows if isinstance(rows, dict) else {}


def check_rows(name, count, absolute_floor, previous=None):
    """Raise FeedError when `count` is below the floor.

    The floor is `absolute_floor`, raised to 90% of `previous` when the
    previous run recorded a positive count.
    """
    minimum = absolute_floor
    if isinstance(previous, int) and not isinstance(previous, bool) and previous > 0:
        minimum = max(minimum, int(previous * MIN_SHARE_OF_PREVIOUS))
    if count < minimum:
        raise FeedError(f"{name}: {count} rows, at least {minimum} required")
    print(f"Validated {name}: {count:,} rows (minimum {minimum:,})")
    return count


def looks_like_html(data):
    """True for an HTML page (an error or login page served as the file)."""
    head = bytes(data[:512]).lstrip(b"\xef\xbb\xbf \t\r\n").lower()
    return head.startswith((b"<!doctype html", b"<html", b"<head", b"<body")) or b"<html" in head


def write_zip(path, entries):
    """Write a deflated ZIP; `entries` maps archive names to source paths."""
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for arcname, source in entries.items():
            zf.write(source, arcname=arcname)
    return Path(path)


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def publish(data_dir, country, version, zip_path, file_count, rows, generated):
    """Move the checked archive into data_dir and write its manifest.

    The archive is named `<prefix>-<version>.zip`; the manifest is
    written after the archive is in place, so it never names a missing
    file. Older archives of the same prefix beyond RETAINED_ARCHIVES are
    deleted. Returns the manifest.
    """
    zip_path = Path(zip_path)
    prefix, _, suffix = zip_path.stem.rpartition("-")
    if not prefix or suffix != version:
        raise FeedError(f"{zip_path.name}: expected <prefix>-{version}.zip")

    manifest = {
        "country": country,
        "version": version,
        "file": zip_path.name,
        "generated": generated.isoformat(),
        "file_count": file_count,
        "sha256": sha256_of(zip_path),
        "size": zip_path.stat().st_size,
        "rows": rows,
    }

    data_dir = Path(data_dir)
    data_dir.mkdir(parents=True, exist_ok=True)
    shutil.move(str(zip_path), str(data_dir / zip_path.name))

    with open(data_dir / "latest.json", "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, indent=2)
        fh.write("\n")

    archives = sorted(data_dir.glob(f"{prefix}-[0-9][0-9][0-9][0-9][0-9][0-9].zip"))
    for old in archives[:-RETAINED_ARCHIVES]:
        old.unlink()

    print(f"Published {data_dir / zip_path.name} ({manifest['size']:,} bytes)")
    return manifest


def decode_text(raw):
    """UTF-8 when the bytes are valid UTF-8, else Windows-1252 (AIFA CSVs)."""
    try:
        return raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return raw.decode("cp1252")


# Validators of the source file, kept in latest.json so the next run can
# ask the source whether the file changed (clients ignore the field).
SOURCE_FIELD = "source"


def conditional_get(session, url, headers, data_dir, force=False):
    """GET `url`, or None when the source answers 304 Not Modified.

    The ETag and Last-Modified of the file behind the published list are
    sent back as If-None-Match / If-Modified-Since, so a daily run on an
    unchanged list costs one small request instead of a full download.
    A source that ignores them answers 200 and the run goes on as
    before. Returns the response, whose validators are passed to
    publish_dated_list or record_source.
    """
    headers = dict(headers)
    source = read_manifest(data_dir).get(SOURCE_FIELD)
    if not force and isinstance(source, dict):
        if isinstance(source.get("etag"), str):
            headers["If-None-Match"] = source["etag"]
        if isinstance(source.get("lastModified"), str):
            headers["If-Modified-Since"] = source["lastModified"]
    response = session.get(url, headers=headers, timeout=120)
    if response.status_code == 304:
        return None
    response.raise_for_status()
    return response


def source_of(response):
    """The validators of a response, for latest.json; None without any."""
    if response is None:
        return None
    source = {}
    if response.headers.get("ETag"):
        source["etag"] = response.headers["ETag"]
    if response.headers.get("Last-Modified"):
        source["lastModified"] = response.headers["Last-Modified"]
    return source or None


def record_source(data_dir, source):
    """Store new validators in latest.json without republishing the list.

    Called when the source file changed but the published list did not
    (another column, a re-export): without it every later run would
    download the file again. Only `source` changes, so clients, which
    compare the list date and SHA-256, see nothing new. True when the
    manifest was rewritten.
    """
    manifest = read_manifest(data_dir)
    if not manifest or not source or manifest.get(SOURCE_FIELD) == source:
        return False
    manifest[SOURCE_FIELD] = source
    _write_manifest(Path(data_dir), manifest)
    return True


def _write_manifest(data_dir, manifest):
    with open(data_dir / "latest.json", "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, indent=2)
        fh.write("\n")


def _published_content(data_dir, manifest):
    """The published list without `generated`, or None if unreadable."""
    name = manifest.get("file")
    if not isinstance(name, str) or "/" in name or "\\" in name:
        return None
    try:
        with open(Path(data_dir) / name, encoding="utf-8") as fh:
            document = json.load(fh)
    except (OSError, ValueError):
        return None
    if not isinstance(document, dict):
        return None
    document.pop("generated", None)
    return document


def dated_list_skip_reason(data_dir, list_date, document, force):
    """Why a dated list must not be published, or None to publish it.

    The version is the list date the source writes in the file, so:
    - an older date than the published one is a stale copy of the
      source (a cache or mirror lagging behind): publishing it would
      roll the feed back, so it is refused even when forced;
    - the same date with the same content is already published, unless
      the run is forced;
    - the same date with other content is a correction the source made
      without changing the date: it is published, and clients holding
      that date download it again because its SHA-256 differs.
    `document` is compared without its `generated` timestamp.
    """
    manifest = read_manifest(data_dir)
    version = f"{list_date:%Y%m%d}"
    published = manifest.get("version")
    if not isinstance(published, str) or len(published) != 8 or not published.isdigit():
        return None
    if version < published:
        return f"list of {list_date} is older than the published list ({published}); refusing to roll back"
    if version > published or force:
        return None
    content = dict(document)
    content.pop("generated", None)
    if _published_content(data_dir, manifest) == content:
        return f"list of {list_date} is already published"
    return None


def publish_dated_list(data_dir, prefix, country, list_date, document, generated, rows,
                       retained=RETAINED_ARCHIVES, source=None):
    """Publish a dated JSON list (the shortage and transparency lists).

    Writes <prefix>-<yyyymmdd>.json (the list date), then latest.json
    with its size and SHA-256, the files the app checks; keeps the
    `retained` newest lists. A republish of the same date gets a new
    SHA-256 (`generated` changes), which clients holding that date
    download again.
    """
    data_dir = Path(data_dir)
    data_dir.mkdir(parents=True, exist_ok=True)
    version = f"{list_date:%Y%m%d}"
    name = f"{prefix}-{version}.json"
    payload = (json.dumps(document, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    (data_dir / name).write_bytes(payload)

    manifest = {
        "country": country,
        "version": version,
        "file": name,
        "generated": generated.isoformat(),
        "sha256": hashlib.sha256(payload).hexdigest(),
        "size": len(payload),
        "rows": rows,
    }
    if source:
        manifest[SOURCE_FIELD] = source
    _write_manifest(data_dir, manifest)

    # Never delete the file latest.json names, even when a forced run
    # publishes a date older than the retained ones.
    files = sorted(data_dir.glob(f"{prefix}-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9].json"))
    for old in [f for f in files if f.name != name][:-(retained - 1) or None]:
        old.unlink()
    print(f"Published {data_dir / name} ({len(payload):,} bytes)")
    return manifest
