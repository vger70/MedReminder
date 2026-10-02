#!/usr/bin/env bash
# Keeps the published feed files on the `feeds` branch, which always
# holds a single parentless commit: every publish replaces it, so the
# repository does not grow by one set of archives a month
# (docs/CATALOGUE-DATA.md §1.1). Clients read
# https://raw.githubusercontent.com/<owner>/<repo>/feeds/data/.
#
#   feeds_branch.sh load             replace data/ in the working tree
#                                    with the branch content (no-op when
#                                    the branch does not exist yet)
#   feeds_branch.sh publish MESSAGE  publish data/ of the working tree as
#                                    the branch's only commit
#
# Run from the repository root of a checkout with push rights, inside
# the catalogue-feeds-publish concurrency group. Only git plumbing on a
# temporary index: the checkout's own index and HEAD are not touched, so
# the step that mirrors data/ to main still sees main's state.
set -euo pipefail

BRANCH="${FEEDS_BRANCH:-feeds}"
REMOTE="${FEEDS_REMOTE:-origin}"
TRACKING="refs/remotes/${REMOTE}/${BRANCH}"

fetch_branch() {
    # ls-remote exits 2 when the branch is absent; any other failure is
    # a real error and stops the run.
    local status=0
    git ls-remote --exit-code --heads "$REMOTE" "$BRANCH" >/dev/null || status=$?
    if [ "$status" -eq 2 ]; then
        git update-ref -d "$TRACKING" 2>/dev/null || true
        return 1
    fi
    [ "$status" -eq 0 ] || exit "$status"
    git fetch --quiet --no-tags --depth 1 "$REMOTE" "+refs/heads/${BRANCH}:${TRACKING}"
}

load() {
    if ! fetch_branch; then
        echo "Branch ${BRANCH} does not exist yet; keeping data/ from the checkout."
        return 0
    fi
    rm -rf data
    git restore --source="$TRACKING" --worktree -- data
    echo "Loaded data/ from ${BRANCH} ($(git rev-parse --short "$TRACKING"))."
}

publish() {
    local message="$1"
    local old=""
    if fetch_branch; then
        old="$(git rev-parse "$TRACKING")"
    fi

    local index
    index="$(mktemp -u)"
    trap 'rm -f "$index"' RETURN

    local readme
    readme="$(printf '%s\n' \
        "Published catalogue feeds (data/<country>/latest.json and archives)." \
        "Written by the download_*.yaml workflows of the main branch; every" \
        "publish replaces the single commit. See docs/CATALOGUE-DATA.md on main." \
        | git hash-object -w --stdin)"

    GIT_INDEX_FILE="$index" git add --all --force -- data
    # The -text rules keep the JSON files byte for byte in any checkout
    # of the branch, as on main.
    if [ -f .gitattributes ]; then
        GIT_INDEX_FILE="$index" git add --force -- .gitattributes
    fi
    GIT_INDEX_FILE="$index" git update-index --add --cacheinfo "100644,${readme},README.md"
    local tree
    tree="$(GIT_INDEX_FILE="$index" git write-tree)"

    if [ -n "$old" ] && [ "$(git rev-parse "${old}^{tree}")" = "$tree" ]; then
        echo "Branch ${BRANCH} already holds this content; nothing to publish."
        return 0
    fi

    local commit
    commit="$(git commit-tree "$tree" -m "$message")"
    # The lease refuses the push if the branch moved since the fetch (an
    # empty lease requires it to be absent), so a concurrent publish is
    # never overwritten silently.
    git push --quiet --force-with-lease="refs/heads/${BRANCH}:${old}" \
        "$REMOTE" "${commit}:refs/heads/${BRANCH}"
    echo "Published ${BRANCH} at $(git rev-parse --short "$commit")."
}

case "${1:-}" in
    load) load ;;
    publish)
        [ -n "${2:-}" ] || { echo "usage: $0 publish MESSAGE" >&2; exit 64; }
        publish "$2"
        ;;
    *) echo "usage: $0 load | publish MESSAGE" >&2; exit 64 ;;
esac
