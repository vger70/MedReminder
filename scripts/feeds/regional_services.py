#!/usr/bin/env python3
"""IT regional services feed: the regional prescription services list.

The list is maintained by hand in scripts/feeds/regional_services_it.json:
one entry per region or autonomous province whose health record service
(web portal, regional app) was checked by a person. This script
validates it and publishes data/it/regional-services/
regional-services-<yyyymmdd>.json (the date of the publish) with a
latest.json manifest, the files the app reads
(docs/prompt/PROMPT-REGIONAL-PRESCRIPTION-SERVICES.md §3.1,
docs/CATALOGUE-DATA.md). The app ships a copy of the source file, so
the source is also a valid list document.

The list is data only: the app opens `webUrl` in the default browser
or shows an app link as a QR code, nothing else. Nothing is ever
guessed: an entry is added only after a person checked its service, and
`showsPrescriptions` stays false until the prescriptions were seen on it.

Usage:
  python scripts/feeds/regional_services.py
      validate the source and publish it when it changed;
  python scripts/feeds/regional_services.py --validate-only
      validate the source only;
  python scripts/feeds/regional_services.py --check-urls REPORT
      request every URL of the source (HTTP status only) and write the
      failures to REPORT as Markdown (an empty file when all answer).
      Never edits the list.
"""

import argparse
import json
import sys
from datetime import date
from pathlib import Path
from urllib.parse import urlsplit

import common

SOURCE = Path(__file__).resolve().parent / "regional_services_it.json"
COUNTRY = "IT"
PREFIX = "regional-services"
DATA_DIR = Path("data/it/regional-services")
RETAINED_FILES = 3

# ISTAT region codes. The two autonomous provinces replace Trentino-Alto
# Adige (04), each with its own health service: 21 Bolzano and 22 Trento,
# as in the national open data that split the region.
REGION_CODES = {
    "01", "02", "03", "05", "06", "07", "08", "09", "10", "11",
    "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22",
}
SIGN_IN = {"SPID", "CIE", "TS-CNS"}
URL_FIELDS = ("webUrl", "iosAppUrl", "androidAppUrl")
MAX_TEXT = 200


class ListError(ValueError):
    """The source list is invalid; the message names the entry."""


def load_source(path=SOURCE):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def _date(value, what):
    if not isinstance(value, str):
        raise ListError(f"{what} is not a yyyy-mm-dd date")
    try:
        return date.fromisoformat(value)
    except ValueError:
        raise ListError(f"{what} is not a yyyy-mm-dd date") from None


def _https(value, what):
    if not isinstance(value, str) or not value:
        raise ListError(f"{what} is not a URL")
    try:
        parts = urlsplit(value)
        port = parts.port
    except ValueError:
        raise ListError(f"{what} is not an https URL") from None
    if parts.scheme != "https" or not parts.hostname:
        raise ListError(f"{what} is not an https URL")
    if parts.username or parts.password or port:
        raise ListError(f"{what} carries credentials or a port")


def _text(entry, field, label):
    value = entry.get(field)
    if not isinstance(value, str) or not value.strip() or len(value) > MAX_TEXT:
        raise ListError(f"{label} has no valid '{field}'")
    return value


def validate(document, today):
    """Raise ListError on the first problem; return the services."""
    if not isinstance(document, dict):
        raise ListError("the list is not a JSON object")
    if document.get("country") != COUNTRY:
        raise ListError(f"the list is not for {COUNTRY}")
    list_date = _date(document.get("listDate"), "'listDate'")
    if list_date > today:
        raise ListError("'listDate' is in the future")
    services = document.get("services")
    if not isinstance(services, list):
        raise ListError("the list has no 'services'")

    seen = set()
    for index, entry in enumerate(services):
        label = f"entry {index + 1}"
        if not isinstance(entry, dict):
            raise ListError(f"{label} is not an object")
        code = entry.get("regionCode")
        if not isinstance(code, str) or not code:
            raise ListError(f"{label} has no 'regionCode'")
        if code not in REGION_CODES:
            raise ListError(f"{label} has an unknown 'regionCode' {code!r}")
        if code in seen:
            raise ListError(f"'regionCode' {code} appears twice")
        seen.add(code)
        label = f"region {code}"
        _text(entry, "region", label)
        _text(entry, "service", label)
        _https(entry.get("webUrl"), f"{label} 'webUrl'")
        for field in ("iosAppUrl", "androidAppUrl"):
            if entry.get(field) is not None:
                _https(entry[field], f"{label} '{field}'")
        sign_in = entry.get("signIn")
        if not isinstance(sign_in, list) or not sign_in:
            raise ListError(f"{label} has no 'signIn'")
        for method in sign_in:
            if method not in SIGN_IN:
                raise ListError(f"{label} has an unknown 'signIn' value {method!r}")
        if len(set(sign_in)) != len(sign_in):
            raise ListError(f"{label} lists a 'signIn' value twice")
        for field in ("showsPrescriptions", "familyDelegation"):
            if not isinstance(entry.get(field), bool):
                raise ListError(f"{label} has no boolean '{field}'")
        if _date(entry.get("verifiedOn"), f"{label} 'verifiedOn'") > today:
            raise ListError(f"{label} 'verifiedOn' is in the future")
    return services


def build_document(source, list_date, generated):
    """The published list: the source with the publish date."""
    document = dict(source)
    document["listDate"] = list_date.isoformat()
    document["generated"] = generated.isoformat()
    return document


def skip_reason(data_dir, document, force):
    """Why the list must not be published, or None to publish it.

    The version is the publish date, so an unchanged source published on
    an earlier day is still current: the content is compared without
    `listDate` and `generated`.
    """
    if force:
        return None
    manifest = common.read_manifest(data_dir)
    published = common._published_content(data_dir, manifest) if manifest else None
    if published is None:
        return None
    content = dict(document)
    for field in ("listDate", "generated"):
        content.pop(field, None)
        published.pop(field, None)
    if published == content:
        return "the list is already published"
    return None


def publish(data_dir, list_date, document, generated):
    """Write regional-services-<yyyymmdd>.json, then latest.json."""
    return common.publish_dated_list(data_dir, PREFIX, COUNTRY, list_date, document, generated,
                                     {"services": len(document["services"])}, RETAINED_FILES)


def urls_of(services):
    """(region code, field, url) for every URL of the list."""
    for entry in services:
        for field in URL_FIELDS:
            if entry.get(field):
                yield entry["regionCode"], field, entry[field]


def check_urls(services, session, timeout=30):
    """(region code, field, url, problem) for each URL that fails.

    HEAD first; a server that refuses HEAD is asked with GET, the body
    never read. A redirect counts as an answer: portals move users to
    their sign-in page.
    """
    failures = []
    for code, field, url in urls_of(services):
        problem = None
        try:
            response = session.head(url, timeout=timeout, allow_redirects=True,
                                    headers=common.browser_headers())
            if response.status_code in (403, 405, 501):
                response = session.get(url, timeout=timeout, allow_redirects=True, stream=True,
                                       headers=common.browser_headers())
                response.close()
            if response.status_code >= 400:
                problem = f"HTTP {response.status_code}"
        except Exception as ex:  # any network failure is reported, never fatal
            problem = type(ex).__name__
        if problem:
            failures.append((code, field, url, problem))
    return failures


def report(failures, checked_on):
    if not failures:
        return ""
    lines = [
        f"The monthly check of {checked_on} found URLs of "
        "`scripts/feeds/regional_services_it.json` that do not answer.",
        "",
        "| Region | Field | URL | Problem |",
        "|---|---|---|---|",
    ]
    lines += [f"| {code} | `{field}` | {url} | {problem} |" for code, field, url, problem in failures]
    lines += [
        "",
        "Check each service by hand and update the list on `main`; the "
        "check never edits it. A temporary outage can be closed without a change.",
    ]
    return "\n".join(lines) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--validate-only", action="store_true", help="validate the source and stop")
    group.add_argument("--check-urls", metavar="REPORT", help="check every URL and write the failures to REPORT")
    parser.add_argument("--source", default=str(SOURCE), help=argparse.SUPPRESS)
    args = parser.parse_args(argv)

    run = common.run_time()
    source = load_source(args.source)
    services = validate(source, run.date())
    print(f"The list is valid: {len(services)} services.")
    if args.validate_only:
        return 0

    if args.check_urls:
        failures = check_urls(services, common.session())
        Path(args.check_urls).write_text(report(failures, run.date().isoformat()), encoding="utf-8")
        print(f"{len(failures)} URL(s) failed.")
        return 0

    document = build_document(source, run.date(), run)
    skip = skip_reason(DATA_DIR, document, common.force_refresh())
    if skip:
        print(f"{skip[0].upper()}{skip[1:]}; nothing to do.")
        return 0
    publish(DATA_DIR, run.date(), document, run)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ListError as ex:
        print(f"Invalid regional services list: {ex}", file=sys.stderr)
        sys.exit(1)
