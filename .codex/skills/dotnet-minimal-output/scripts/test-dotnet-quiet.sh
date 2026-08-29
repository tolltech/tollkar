#!/bin/sh
set -eu

# Exercise success, failure, quoting, and argument validation without a real SDK.
script_dir=$(CDPATH='' cd -- "$(dirname "$0")" && pwd)
target_script="$script_dir/dotnet-quiet.sh"
work_dir=$(mktemp -d)
trap 'rm -rf "$work_dir"' EXIT HUP INT TERM

assert_contains() {
  rg -q --fixed-strings -- "$1" "$2"
}

fake_bin="$work_dir/bin"
results_dir="$work_dir/results"
mkdir -p "$fake_bin" "$results_dir"

cat > "$fake_bin/dotnet" <<'EOF'
#!/bin/sh
case "$1" in
  test-ok)
    echo "ok stdout"
    echo "ok stderr" >&2
    exit 0
    ;;
  test-fail)
    echo "bad stdout"
    echo "bad stderr" >&2
    exit 7
    ;;
  slow-ok)
    sleep 1
    echo "slow stdout"
    exit 0
    ;;
  record-args)
    printf '%s\n' "$2"
    exit 0
    ;;
  *)
    echo "unexpected args: $*" >&2
    exit 9
    ;;
esac
EOF
chmod +x "$fake_bin/dotnet"

cat > "$fake_bin/date" <<'EOF'
#!/bin/sh
printf '20260101T000000Z\n'
EOF
chmod +x "$fake_bin/date"

PATH="$fake_bin:$PATH"
export PATH

success_console="$work_dir/success.console"
failure_console="$work_dir/failure.console"

sh "$target_script" --label success-case --results-dir "$results_dir" -- dotnet test-ok \
  >"$success_console" 2>&1

success_run=$(find "$results_dir" -mindepth 1 -maxdepth 1 -type d -name '*-success-case' | head -n 1)
[ -n "$success_run" ]
assert_contains "ok stdout" "$success_run/stdout.log"
assert_contains "ok stderr" "$success_run/stderr.log"
assert_contains "success" "$success_console"

repo_case="$work_dir/repo-case"
mkdir -p "$repo_case"
(cd "$repo_case" && git init -q .)
repo_console="$work_dir/repo.console"
(cd "$repo_case" && sh "$target_script" --label repo-root-default -- dotnet test-ok >"$repo_console" 2>&1)

repo_run=$(find "$repo_case/_tmp/dotnet" -mindepth 1 -maxdepth 1 -type d -name '*-repo-root-default' | head -n 1)
[ -n "$repo_run" ]
assert_contains "ok stdout" "$repo_run/stdout.log"

sh "$target_script" --label repeat-case --results-dir "$results_dir" -- dotnet test-ok >/dev/null 2>&1
sh "$target_script" --label repeat-case --results-dir "$results_dir" -- dotnet test-ok >/dev/null 2>&1
repeat_count=$(find "$results_dir" -mindepth 1 -maxdepth 1 -type d -name '20260101T000000Z-repeat-case*' | wc -l | tr -d ' ')
[ "$repeat_count" -eq 2 ]

sh "$target_script" --label parallel-case --results-dir "$results_dir" -- dotnet slow-ok >/dev/null 2>&1 &
pid_one=$!
sh "$target_script" --label parallel-case --results-dir "$results_dir" -- dotnet slow-ok >/dev/null 2>&1 &
pid_two=$!
wait "$pid_one"
wait "$pid_two"
parallel_count=$(find "$results_dir" -mindepth 1 -maxdepth 1 -type d -name '20260101T000000Z-parallel-case*' | wc -l | tr -d ' ')
[ "$parallel_count" -eq 2 ]

quoted_console="$work_dir/quoted.console"
sh "$target_script" --label quoted-case --results-dir "$results_dir" -- \
  dotnet record-args "path with spaces.csproj" >"$quoted_console" 2>&1

quoted_run=$(find "$results_dir" -mindepth 1 -maxdepth 1 -type d -name '*-quoted-case' | head -n 1)
[ -n "$quoted_run" ]
assert_contains "path with spaces.csproj" "$quoted_run/stdout.log"
assert_contains "'path with spaces.csproj'" "$quoted_run/command.txt"

set +e
sh "$target_script" --label failure-case --results-dir "$results_dir" --tail-lines 5 -- dotnet test-fail \
  >"$failure_console" 2>&1
status=$?
set -e

[ "$status" -eq 7 ]
failure_run=$(find "$results_dir" -mindepth 1 -maxdepth 1 -type d -name '*-failure-case' | head -n 1)
[ -n "$failure_run" ]
assert_contains "bad stdout" "$failure_run/stdout.log"
assert_contains "bad stderr" "$failure_run/stderr.log"
assert_contains "failed with exit code 7" "$failure_console"
assert_contains "tail of stderr.log" "$failure_console"

invalid_console="$work_dir/invalid.console"
set +e
sh "$target_script" --results-dir "$results_dir" --tail-lines nope -- dotnet test-fail \
  >"$invalid_console" 2>&1
status=$?
set -e

[ "$status" -eq 2 ]
assert_contains "usage:" "$invalid_console"

echo "dotnet-quiet.sh tests passed"
