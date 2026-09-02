#!/bin/sh

# Build and copy web files without managing the service or migrating databases.
set -eu
umask 077

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd -P)
. "$repo_dir/publish-common.sh"

[ "$#" -eq 1 ] && [ -n "$1" ] || publication_fail "Usage: $0 /absolute/or/relative/deployment-directory"
require_publication_tools dotnet npm rsync
resolve_publication_destination "$1"

lock_dir=
stage_dir=
cleanup() {
    status=$?
    cleanup_publication_artifacts
    exit "$status"
}
trap cleanup 0
trap 'exit 1' HUP INT TERM

acquire_publication_lock
create_publication_stage
build_web_publication
copy_web_publication

printf '%s\n' "Published files successfully: $destination" \
    "LaunchAgent and databases were not changed." \
    "A running server keeps its loaded binaries until it is restarted manually."
