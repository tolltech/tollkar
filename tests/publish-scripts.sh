#!/bin/sh

# Exercise publication orchestration with deterministic local command doubles.
set -eu

repo_dir=$(CDPATH= cd "$(dirname "$0")/.." && pwd -P)
test_dir=$(mktemp -d "${TMPDIR:-/tmp}/tollkar-publish-tests.XXXXXX")
cleanup() {
    rm -rf "$test_dir"
}
trap cleanup 0
trap 'exit 1' HUP INT TERM

fake_bin=$test_dir/bin
test_home=$test_dir/home
destination=$test_dir/server
state_dir=$test_dir/state
test_tmp=$test_dir/tmp
mkdir -p "$fake_bin" "$test_home/Library/LaunchAgents" "$destination" "$state_dir" "$test_tmp"
destination=$(CDPATH= cd "$destination" && pwd -P)
: >"$test_home/Library/LaunchAgents/local.tollkar.web.plist"

create_fake() {
    name=$1
    shift
    file=$fake_bin/$name
    apply_body=$*
    printf '%s\n' '#!/bin/sh' 'set -eu' "$apply_body" >"$file"
    chmod +x "$file"
}

create_fake plutil '
case "$*" in
    *" Label "*) printf "%s\n" local.tollkar.web ;;
    *" WorkingDirectory "*) printf "%s\n" "$TEST_DESTINATION" ;;
    *) exit 1 ;;
esac'

create_fake launchctl '
printf "%s\n" "$*" >>"$TEST_CALLS"
case "$1" in
    print)
        [ -f "$TEST_STATE_DIR/loaded" ] || exit 1
        printf "%s\n" "service = local.tollkar.web"
        ;;
    bootout)
        rm -f "$TEST_STATE_DIR/loaded"
        ;;
    bootstrap)
        : >"$TEST_STATE_DIR/loaded"
        ;;
    kickstart) ;;
    *) exit 1 ;;
esac'

create_fake dotnet '
printf "%s\n" "$*" >>"$TEST_CALLS"
if [ "$1" = publish ]; then
    [ "${TEST_FAIL_BUILD:-0}" -eq 0 ] || exit 1
    output=
    previous=
    for argument in "$@"; do
        if [ "$previous" = --output ]; then output=$argument; fi
        previous=$argument
    done
    [ -n "$output" ]
    mkdir -p "$output/wwwroot"
    mkdir -p "$output/songs" "$output/logs" "$output/Logs" "$output/data" "$output/keys" "$output/backups"
    printf "%s\n" binary >"$output/Tollkar.Web.dll"
    printf "%s\n" generated >"$output/appsettings.json"
    printf "%s\n" generated >"$output/appsettings.Production.json"
    printf "%s\n" generated >"$output/tollkar-web.db"
    printf "%s\n" generated >"$output/tollkar-library.sqlite-shm"
    printf "%s\n" generated >"$output/songs/song.mp4"
    printf "%s\n" generated >"$output/logs/web.log"
    printf "%s\n" generated >"$output/Logs/web.log"
    printf "%s\n" generated >"$output/data/value"
    printf "%s\n" generated >"$output/keys/key"
    printf "%s\n" generated >"$output/backups/backup"
    printf "%s\n" frontend >"$output/wwwroot/index.html"
    exit 0
fi
[ "${2:-}" = --migrate-databases ]
[ "${TEST_FAIL_MIGRATION:-0}" -eq 0 ]'

create_fake npm 'exit 0'
create_fake curl '
printf "%s\n" "$*" >>"$TEST_CALLS"
exit 0'

export PATH="$fake_bin:$PATH"
export HOME=$test_home
export TMPDIR=$test_tmp
export TEST_DESTINATION=$destination
export TEST_STATE_DIR=$state_dir
export TEST_CALLS=$test_dir/calls

reset_case() {
    rm -f "$TEST_CALLS" "$state_dir/loaded"
    rm -rf "$destination"
    mkdir -p "$destination"
}

assert_called() {
    pattern=$1
    grep -q "$pattern" "$TEST_CALLS" || {
        printf '%s\n' "Expected call matching '$pattern'." >&2
        exit 1
    }
}

assert_not_called() {
    pattern=$1
    if [ -f "$TEST_CALLS" ] && grep -q "$pattern" "$TEST_CALLS"; then
        printf '%s\n' "Unexpected call matching '$pattern'." >&2
        exit 1
    fi
}

run_success() {
    case_log=$test_dir/case.log
    if ! "$@" >"$case_log" 2>&1; then
        cat "$case_log" >&2
        exit 1
    fi
}

run_failure() {
    case_log=$test_dir/case.log
    if "$@" >"$case_log" 2>&1; then
        cat "$case_log" >&2
        printf '%s\n' 'Expected publication to fail.' >&2
        exit 1
    fi
}

reset_case
run_success sh "$repo_dir/publish.sh" "$destination"
assert_not_called '^bootout '
assert_called '^bootstrap '
assert_called '^kickstart '
assert_called 'Tollkar.Web.dll --migrate-databases'

reset_case
: >"$state_dir/loaded"
run_success sh "$repo_dir/publish.sh" "$destination"
assert_called '^bootout '
assert_called '^bootstrap '

reset_case
export TEST_FAIL_BUILD=1
run_failure sh "$repo_dir/publish.sh" "$destination"
unset TEST_FAIL_BUILD
assert_not_called '^bootout '
assert_not_called '^bootstrap '
[ ! -d "$destination/.publish-lock" ]

reset_case
: >"$state_dir/loaded"
export TEST_FAIL_BUILD=1
run_failure sh "$repo_dir/publish.sh" "$destination"
unset TEST_FAIL_BUILD
assert_not_called '^bootout '
assert_not_called '^bootstrap '
[ -f "$state_dir/loaded" ]
[ ! -d "$destination/.publish-lock" ]

reset_case
export TEST_FAIL_MIGRATION=1
run_failure sh "$repo_dir/publish.sh" "$destination"
unset TEST_FAIL_MIGRATION
assert_not_called '^bootout '
assert_not_called '^bootstrap '
[ ! -d "$destination/.publish-lock" ]

reset_case
: >"$state_dir/loaded"
export TEST_FAIL_MIGRATION=1
run_failure sh "$repo_dir/publish.sh" "$destination"
unset TEST_FAIL_MIGRATION
assert_called '^bootout '
assert_not_called '^bootstrap '
[ ! -f "$state_dir/loaded" ]
[ ! -d "$destination/.publish-lock" ]

reset_case
printf '%s\n' existing >"$destination/appsettings.json"
printf '%s\n' database >"$destination/tollkar-web.db"
printf '%s\n' database >"$destination/tollkar-library.sqlite-shm"
mkdir -p "$destination/songs" "$destination/logs" "$destination/Logs" \
    "$destination/data" "$destination/keys" "$destination/backups"
printf '%s\n' song >"$destination/songs/song.mp4"
printf '%s\n' log >"$destination/logs/web.log"
printf '%s\n' log >"$destination/Logs/web.log"
printf '%s\n' data >"$destination/data/value"
printf '%s\n' key >"$destination/keys/key"
printf '%s\n' backup >"$destination/backups/backup"
run_success sh "$repo_dir/publish-files.sh" "$destination"
assert_not_called '^print '
assert_not_called '^bootout '
assert_not_called '^bootstrap '
assert_not_called 'migrate-databases'
assert_not_called '^--connect-timeout '
[ "$(cat "$destination/appsettings.json")" = existing ]
[ "$(cat "$destination/appsettings.Production.json")" = generated ]
[ "$(cat "$destination/tollkar-web.db")" = database ]
[ "$(cat "$destination/tollkar-library.sqlite-shm")" = database ]
[ "$(cat "$destination/songs/song.mp4")" = song ]
[ "$(cat "$destination/logs/web.log")" = log ]
[ "$(cat "$destination/Logs/web.log")" = log ]
[ "$(cat "$destination/data/value")" = data ]
[ "$(cat "$destination/keys/key")" = key ]
[ "$(cat "$destination/backups/backup")" = backup ]
[ "$(cat "$destination/Tollkar.Web.dll")" = binary ]
[ "$(cat "$destination/wwwroot/index.html")" = frontend ]
[ ! -d "$destination/.publish-lock" ]

reset_case
export TEST_FAIL_BUILD=1
run_failure sh "$repo_dir/publish-files.sh" "$destination"
unset TEST_FAIL_BUILD
[ ! -d "$destination/.publish-lock" ]

set -- "$test_tmp"/tollkar-publish.*
[ ! -e "$1" ]

printf '%s\n' 'Publication script tests passed.'
