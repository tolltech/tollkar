#!/bin/sh

# Publish separately from persistent server data. Stop the server before running.
set -eu
umask 077

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

[ "$#" -eq 1 ] && [ -n "$1" ] || fail "Usage: $0 /absolute/or/relative/deployment-directory"
for tool in dotnet npm rsync; do
    command -v "$tool" >/dev/null 2>&1 || fail "Required tool not found: $tool"
done

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd -P)
case "$1" in
    /*) destination=$1 ;;
    *) destination=$PWD/$1 ;;
esac
mkdir -p "$destination"
destination=$(CDPATH= cd "$destination" && pwd -P)
case "$destination/" in
    "$repo_dir/"*) fail "The deployment directory must be outside the repository." ;;
esac
case "$repo_dir/" in
    "$destination/"*) fail "The deployment directory cannot be an ancestor of the repository." ;;
esac

# A directory lock prevents two publishers from updating the same installation.
lock_dir="$destination/.publish-lock"
mkdir "$lock_dir" 2>/dev/null || fail "Publication is already locked: $lock_dir"
stage_dir=
cleanup() {
    [ -z "$stage_dir" ] || rm -rf "$stage_dir"
    rmdir "$lock_dir"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
stage_dir=$(mktemp -d "${TMPDIR:-/tmp}/tollkar-publish.XXXXXX")

printf '%s\n' "Building web application; deployment directory: $destination"
cd "$repo_dir"
sh "$repo_dir/.codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh" \
    --label publish-web -- dotnet publish src/Tollkar.Web/Tollkar.Web.csproj \
    --configuration Release --no-self-contained --nologo --verbosity minimal --output "$stage_dir"

# Never delete destination files or copy development databases, media or logs.
# Existing server configuration wins; new configuration files are seeded once.
rsync -a --ignore-existing --include='/appsettings*.json' --exclude='*' "$stage_dir/" "$destination/"
rsync -a \
    --exclude='appsettings*.json' --exclude='*.db*' --exclude='*.sqlite*' \
    --exclude='songs/' --exclude='logs/' --exclude='Logs/' --exclude='*.log*' \
    --exclude='data/' --exclude='keys/' --exclude='backups/' \
    "$stage_dir/" "$destination/"

mkdir -p "$destination/songs" "$destination/logs"
# Relative SQLite paths must resolve exactly as they do for the deployed server.
# Explicit migration mode never starts hosted services or scans the song directory.
cd "$destination"
ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_CONTENTROOT="$destination" DOTNET_CONTENTROOT="$destination" \
    dotnet ./Tollkar.Web.dll --migrate-databases
printf '%s\n' "Published and migrated successfully: $destination" \
    'Start from this directory: ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production dotnet Tollkar.Web.dll --urls http://127.0.0.1:5080'
