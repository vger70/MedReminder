#!/usr/bin/env bash
# Keeps the published feed files on the `feeds` branch, which always
# holds a single parentless commit: every publish replaces it, so the
# repository does not grow by one set of archives a month
# (docs/CATALOGUE-DATA.md §1.1). Clients read
# https://raw.githubusercontent.com/<owner>/<repo>/feeds/data/.
#
#   feeds_branch.sh load
#       Replace data/ in the working tree with the branch content (no-op
#       when the branch does not exist yet).
#   feeds_branch.sh publish MESSAGE PATHSPEC...
#       Publish the paths the calling workflow owns (for example data/es,
#       or data/it with its shortages and equivalents excluded) on top of
#       the branch as it is at push time, as the branch's only commit.
#
# Each workflow has its own concurrency group, so two feeds can publish
# at the same time: a publish starts from the current branch, replaces
# only its own paths and pushes with --force-with-lease; when another
# publish lands first, the lease fails and the publish is rebuilt on the
# new branch. Only git plumbing on a temporary index: the checkout's own
# index and HEAD are not touched, so the step that mirrors data/ to main
# still sees main's state.
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

# One attempt, run in a subshell with errexit on: 0 published or
# nothing to publish, 3 lease lost (the branch moved), anything else is
# an error.
publish_once() {
    local index="$1" message="$2"
    shift 2
    local old=""
    if fetch_branch; then
        old="$(git rev-parse "$TRACKING")"
    fi

    local readme
    readme="$(printf '%s\n' \
        "Published catalogue feeds (data/<country>/latest.json and archives)." \
        "Written by the download_*.yaml workflows of the main branch; every" \
        "publish replaces the single commit. See docs/CATALOGUE-DATA.md on main." \
        | git hash-object -w --stdin)"

    if [ -n "$old" ]; then
        # Start from the published branch and replace only the paths this
        # workflow owns, so a feed published since this job's load is kept.
        GIT_INDEX_FILE="$index" git read-tree "$old"
        GIT_INDEX_FILE="$index" git rm -r -q --cached --ignore-unmatch -- "$@"
        GIT_INDEX_FILE="$index" git add --all --force -- "$@"
    else
        # First publish: the branch starts from every feed of the checkout.
        GIT_INDEX_FILE="$index" git add --all --force -- data
    fi
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
    # empty lease requires it to be absent): the attempt is then rebuilt
    # on the new branch instead of overwriting the other publish.
    if ! git push --quiet --force-with-lease="refs/heads/${BRANCH}:${old}" \
        "$REMOTE" "${commit}:refs/heads/${BRANCH}"; then
        return 3
    fi
    echo "Published ${BRANCH} at $(git rev-parse --short "$commit")."
}

publish() {
    local attempt status index
    for attempt in 1 2 3 4 5; do
        index="$(mktemp -u)"
        # A function called from `||` or `if` runs without errexit, even
        # if it sets it again: the subshell keeps every failure fatal.
        set +e
        (set -e; publish_once "$index" "$@")
        status=$?
        set -e
        rm -f "$index"
        [ "$status" -eq 3 ] || return "$status"
        echo "Branch ${BRANCH} moved during the publish; retrying (${attempt}/5)."
        sleep $((attempt * 2))
    done
    echo "Branch ${BRANCH} kept moving; giving up." >&2
    return 1
}

case "${1:-}" in
    load) load ;;
    publish)
        [ -n "${2:-}" ] && [ -n "${3:-}" ] || { echo "usage: $0 publish MESSAGE PATHSPEC..." >&2; exit 64; }
        shift
        publish "$@"
        ;;
    *) echo "usage: $0 load | publish MESSAGE PATHSPEC..." >&2; exit 64 ;;
esac
