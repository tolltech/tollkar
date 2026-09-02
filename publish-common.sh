#!/bin/sh

# Shared build and copy primitives for web publication entrypoints.

publication_fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

require_publication_tools() {
    for publication_tool in "$@"; do
        command -v "$publication_tool" >/dev/null 2>&1 ||
            publication_fail "Required tool not found: $publication_tool"
    done
}

resolve_publication_destination() {
    [ "$#" -eq 1 ] && [ -n "$1" ] ||
        publication_fail "A deployment directory is required."
    case "$1" in
        /*) destination=$1 ;;
        *) destination=$PWD/$1 ;;
    esac
    mkdir -p "$destination"
    destination=$(CDPATH= cd "$destination" && pwd -P)
    case "$destination/" in
        "$repo_dir/"*) publication_fail "The deployment directory must be outside the repository." ;;
    esac
    case "$repo_dir/" in
        "$destination/"*) publication_fail "The deployment directory cannot be an ancestor of the repository." ;;
    esac
}

acquire_publication_lock() {
    lock_dir=$destination/.publish-lock
    mkdir "$lock_dir" 2>/dev/null || publication_fail "Publication is already locked: $lock_dir"
}

create_publication_stage() {
    stage_dir=$(mktemp -d "${TMPDIR:-/tmp}/tollkar-publish.XXXXXX")
}

cleanup_publication_artifacts() {
    [ -z "${stage_dir:-}" ] || rm -rf "$stage_dir" || :
    [ -z "${lock_dir:-}" ] || rmdir "$lock_dir" 2>/dev/null || :
}

build_web_publication() {
    printf '%s\n' "Building web application; deployment directory: $destination"
    (
        cd "$repo_dir"
        sh "$repo_dir/.codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh" \
            --label publish-web -- dotnet publish src/Tollkar.Web/Tollkar.Web.csproj \
            --configuration Release --no-self-contained --nologo --verbosity minimal --output "$stage_dir"
    )
}

copy_web_publication() {
    # Preserve server configuration and all persistent data while refreshing application files.
    printf '%s\n' "Copying published files to: $destination"
    rsync -a --ignore-existing --include='/appsettings*.json' --exclude='*' "$stage_dir/" "$destination/"
    rsync -a \
        --exclude='appsettings*.json' --exclude='*.db*' --exclude='*.sqlite*' \
        --exclude='songs/' --exclude='logs/' --exclude='Logs/' --exclude='*.log*' \
        --exclude='data/' --exclude='keys/' --exclude='backups/' \
        "$stage_dir/" "$destination/"
    mkdir -p "$destination/songs" "$destination/logs"
}
