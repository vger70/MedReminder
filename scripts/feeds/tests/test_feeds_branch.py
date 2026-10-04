import os
import shutil
import subprocess
from pathlib import Path

import pytest

from conftest import FEEDS_DIR

SCRIPT = FEEDS_DIR / "feeds_branch.sh"

pytestmark = pytest.mark.skipif(
    shutil.which("bash") is None or shutil.which("git") is None,
    reason="needs bash and git",
)


def git(cwd, *args):
    return subprocess.run(
        ["git", *args], cwd=cwd, check=True, capture_output=True, text=True
    ).stdout.strip()


def run_script(cwd, *args):
    return subprocess.run(
        ["bash", str(SCRIPT), *args], cwd=cwd, check=True, capture_output=True, text=True
    ).stdout


@pytest.fixture
def checkout(tmp_path, monkeypatch):
    """A clone of a bare remote whose main branch carries data/it."""
    for name in ("GIT_DIR", "GIT_INDEX_FILE", "GIT_WORK_TREE"):
        monkeypatch.delenv(name, raising=False)
    monkeypatch.setenv("GIT_CONFIG_GLOBAL", os.devnull)
    monkeypatch.setenv("GIT_CONFIG_NOSYSTEM", "1")

    remote = tmp_path / "remote.git"
    git(tmp_path, "init", "--quiet", "--bare", "--initial-branch=main", str(remote))

    work = tmp_path / "work"
    git(tmp_path, "clone", "--quiet", str(remote), str(work))
    git(work, "config", "user.name", "test")
    git(work, "config", "user.email", "test@example.invalid")
    git(work, "checkout", "--quiet", "-b", "main")
    (work / "data" / "it").mkdir(parents=True)
    (work / "data" / "it" / "latest.json").write_text('{"version": "202609"}\n')
    (work / "README.md").write_text("main\n")
    (work / ".gitattributes").write_text("data/**/*.json -text\n")
    git(work, "add", ".")
    git(work, "commit", "--quiet", "-m", "main")
    git(work, "push", "--quiet", "origin", "main")
    return work, remote


def feeds_files(remote):
    listing = git(remote, "ls-tree", "-r", "--name-only", "feeds")
    return sorted(listing.splitlines())


def test_load_without_branch_keeps_the_checkout(checkout):
    work, _ = checkout
    out = run_script(work, "load")
    assert "does not exist yet" in out
    assert (work / "data" / "it" / "latest.json").read_text() == '{"version": "202609"}\n'


def test_first_publish_creates_a_parentless_branch_with_data_only(checkout):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")

    assert feeds_files(remote) == [".gitattributes", "README.md", "data/it/latest.json"]
    assert git(remote, "rev-list", "--count", "feeds") == "1"
    assert git(remote, "show", "feeds:README.md").startswith("Published catalogue feeds")
    # main is untouched: same commit, clean index.
    assert git(remote, "rev-list", "--count", "main") == "1"
    assert git(work, "status", "--porcelain") == ""


def test_publish_replaces_the_single_commit(checkout):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")
    first = git(remote, "rev-parse", "feeds")

    (work / "data" / "es").mkdir()
    (work / "data" / "es" / "latest.json").write_text('{"version": "202610"}\n')
    run_script(work, "publish", "Feeds 2", "data/es")

    assert git(remote, "rev-parse", "feeds") != first
    assert git(remote, "rev-list", "--count", "feeds") == "1"
    assert git(remote, "log", "-1", "--format=%s", "feeds") == "Feeds 2"
    assert feeds_files(remote) == [".gitattributes", "README.md", "data/es/latest.json", "data/it/latest.json"]


def test_publish_without_changes_keeps_the_commit(checkout):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")
    first = git(remote, "rev-parse", "feeds")

    out = run_script(work, "publish", "Feeds 2", "data/it")

    assert "nothing to publish" in out
    assert git(remote, "rev-parse", "feeds") == first


def test_load_replaces_data_with_the_branch_content(checkout):
    work, remote = checkout
    (work / "data" / "fr").mkdir()
    (work / "data" / "fr" / "latest.json").write_text('{"version": "202610"}\n')
    run_script(work, "publish", "Feeds 1", "data/it")
    shutil.rmtree(work / "data" / "fr")
    (work / "data" / "it" / "stale.zip").write_bytes(b"stale")

    run_script(work, "load")

    assert (work / "data" / "fr" / "latest.json").exists()
    assert not (work / "data" / "it" / "stale.zip").exists()
    # The working tree changes; the index still matches main.
    assert git(work, "diff", "--cached", "--name-only") == ""


def test_load_then_publish_keeps_files_published_by_another_job(checkout, tmp_path):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")

    # Another workflow publishes data/eu from its own checkout of main.
    other = tmp_path / "other"
    git(tmp_path, "clone", "--quiet", "--branch", "main", str(remote), str(other))
    git(other, "config", "user.name", "test")
    git(other, "config", "user.email", "test@example.invalid")
    run_script(other, "load")
    (other / "data" / "eu").mkdir()
    (other / "data" / "eu" / "latest.json").write_text("{}\n")
    run_script(other, "publish", "Feeds eu", "data/eu")

    # This job loads first, as the workflows do, then adds data/es.
    run_script(work, "load")
    (work / "data" / "es").mkdir()
    (work / "data" / "es" / "latest.json").write_text("{}\n")
    run_script(work, "publish", "Feeds es", "data/es")

    assert feeds_files(remote) == [
        ".gitattributes", "README.md", "data/es/latest.json", "data/eu/latest.json", "data/it/latest.json",
    ]
    assert git(remote, "rev-list", "--count", "feeds") == "1"


def test_publish_keeps_a_feed_published_after_this_job_loaded(checkout, tmp_path):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")

    # This job loads, then another feed publishes before this job does.
    run_script(work, "load")
    other = tmp_path / "other"
    git(tmp_path, "clone", "--quiet", "--branch", "main", str(remote), str(other))
    git(other, "config", "user.name", "test")
    git(other, "config", "user.email", "test@example.invalid")
    run_script(other, "load")
    (other / "data" / "eu").mkdir()
    (other / "data" / "eu" / "latest.json").write_text("{}\n")
    run_script(other, "publish", "Feeds eu", "data/eu")

    (work / "data" / "es").mkdir()
    (work / "data" / "es" / "latest.json").write_text("{}\n")
    run_script(work, "publish", "Feeds es", "data/es")

    assert feeds_files(remote) == [
        ".gitattributes", "README.md", "data/es/latest.json", "data/eu/latest.json", "data/it/latest.json",
    ]


def test_publish_replaces_only_the_owned_paths(checkout):
    work, remote = checkout
    (work / "data" / "it" / "shortages").mkdir()
    (work / "data" / "it" / "shortages" / "latest.json").write_text('{"version": "20260929"}\n')
    run_script(work, "publish", "Feeds 1", "data/it")

    # The catalogue job's checkout holds a stale shortage list and a new
    # catalogue manifest; it owns data/it without the shortages.
    (work / "data" / "it" / "latest.json").write_text('{"version": "202610"}\n')
    (work / "data" / "it" / "shortages" / "latest.json").write_text('{"version": "20260901"}\n')
    run_script(work, "publish", "Feeds 2", "data/it", ":(exclude)data/it/shortages")

    assert git(remote, "show", "feeds:data/it/latest.json") == '{"version": "202610"}'
    assert git(remote, "show", "feeds:data/it/shortages/latest.json") == '{"version": "20260929"}'


def test_publish_removes_files_deleted_in_an_owned_path(checkout):
    work, remote = checkout
    (work / "data" / "it" / "old.zip").write_bytes(b"old")
    run_script(work, "publish", "Feeds 1", "data/it")

    (work / "data" / "it" / "old.zip").unlink()
    run_script(work, "publish", "Feeds 2", "data/it")

    assert "data/it/old.zip" not in feeds_files(remote)


def test_publish_requires_a_pathspec(checkout):
    work, _ = checkout
    result = subprocess.run(["bash", str(SCRIPT), "publish", "Feeds"], cwd=work, capture_output=True, text=True)
    assert result.returncode == 64


def test_publish_retries_when_the_push_is_rejected(checkout):
    work, remote = checkout
    run_script(work, "publish", "Feeds 1", "data/it")

    # The remote rejects the next push once, as a lost lease does.
    marker = remote / "rejected-once"
    hook = remote / "hooks" / "pre-receive"
    hook.write_text(f'#!/bin/sh\nif [ ! -e "{marker}" ]; then touch "{marker}"; exit 1; fi\n')
    hook.chmod(0o755)

    (work / "data" / "it" / "latest.json").write_text('{"version": "202610"}\n')
    out = run_script(work, "publish", "Feeds 2", "data/it")

    assert "retrying (1/5)" in out
    assert git(remote, "log", "-1", "--format=%s", "feeds") == "Feeds 2"
