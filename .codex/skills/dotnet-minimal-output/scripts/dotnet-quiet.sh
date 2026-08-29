#!/bin/sh
set -eu

# Wrap noisy dotnet commands so the terminal stays short and logs stay inspectable.
usage() {
  echo "usage: $0 [--label NAME] [--results-dir DIR] [--tail-lines N] -- dotnet <args...>" >&2
  exit 2
}

require_value() {
  [ "$#" -ge 2 ] || usage
}

slugify() {
  printf '%s' "$1" | tr -cs 'A-Za-z0-9._:' '-'
}

is_unsigned_integer() {
  case "$1" in
    ''|*[!0-9]*)
      return 1
      ;;
    *)
      return 0
      ;;
  esac
}

quote_arg() {
  escaped=$(printf '%s' "$1" | sed "s/'/'\\\\''/g")
  printf "'%s'" "$escaped"
}

write_command_file() {
  first=1
  for arg in "$@"; do
    if [ "$first" -eq 1 ]; then
      quote_arg "$arg"
      first=0
    else
      printf ' '
      quote_arg "$arg"
    fi
  done
  printf '\n'
}

print_tail() {
  file=$1
  lines=$2
  if [ ! -s "$file" ]; then
    return 0
  fi
  echo "[dotnet-quiet] tail of $(basename "$file"):"
  tail -n "$lines" "$file"
}

allocate_run_dir() {
  base_dir=$1
  candidate=$base_dir
  suffix=1
  while ! mkdir "$candidate" 2>/dev/null; do
    suffix=$((suffix + 1))
    candidate="${base_dir}-${suffix}"
  done
  printf '%s\n' "$candidate"
}

label=
results_dir=
tail_lines=40

while [ "$#" -gt 0 ]; do
  case "$1" in
    --label)
      require_value "$@"
      label=$2
      shift 2
      ;;
    --results-dir)
      require_value "$@"
      results_dir=$2
      shift 2
      ;;
    --tail-lines)
      require_value "$@"
      is_unsigned_integer "$2" || usage
      tail_lines=$2
      shift 2
      ;;
    --help|-h)
      usage
      ;;
    --)
      shift
      break
      ;;
    *)
      usage
      ;;
  esac
done

[ "$#" -gt 0 ] || usage
[ "$1" = "dotnet" ] || usage

if [ -z "$results_dir" ]; then
  # Prefer a repo-root _tmp directory so logs stay in one predictable place.
  repo_root=$(git rev-parse --show-toplevel 2>/dev/null || printf '.')
  results_dir="$repo_root/_tmp/dotnet"
fi

if [ -z "$label" ]; then
  if [ "$#" -ge 2 ]; then
    label=$2
  else
    label=$1
  fi
fi

timestamp=$(date -u '+%Y%m%dT%H%M%SZ')
safe_label=$(slugify "$label")
mkdir -p "$results_dir"
run_dir=$(allocate_run_dir "${results_dir%/}/${timestamp}-${safe_label}")
stdout_log="$run_dir/stdout.log"
stderr_log="$run_dir/stderr.log"
command_file="$run_dir/command.txt"
status_file="$run_dir/exit-code.txt"
write_command_file "$@" > "$command_file"

echo "[dotnet-quiet] running: $(cat "$command_file")"
echo "[dotnet-quiet] logs: $run_dir"

if "$@" >"$stdout_log" 2>"$stderr_log"; then
  status=0
else
  status=$?
fi

printf '%s\n' "$status" > "$status_file"

if [ "$status" -eq 0 ]; then
  echo "[dotnet-quiet] success"
  exit 0
fi

echo "[dotnet-quiet] failed with exit code $status"
print_tail "$stderr_log" "$tail_lines"
if [ ! -s "$stderr_log" ]; then
  print_tail "$stdout_log" "$tail_lines"
fi
exit "$status"
