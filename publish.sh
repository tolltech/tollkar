#!/bin/sh

# Publish separately from persistent server data and restart its launchd service.
set -eu
umask 077

agent_label=local.tollkar.web
health_url=http://127.0.0.1:5080/api/health

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

[ "$#" -eq 1 ] && [ -n "$1" ] || fail "Usage: $0 /absolute/or/relative/deployment-directory"
for tool in awk curl date dotnet launchctl npm plutil rsync; do
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

agent_plist=${HOME:?}/Library/LaunchAgents/$agent_label.plist
agent_domain=gui/$(id -u)
agent_target=$agent_domain/$agent_label
[ -f "$agent_plist" ] || fail "LaunchAgent not found: $agent_plist"
[ "$(plutil -extract Label raw -o - "$agent_plist")" = "$agent_label" ] ||
    fail "LaunchAgent label must be $agent_label: $agent_plist"
agent_working_directory=$(plutil -extract WorkingDirectory raw -o - "$agent_plist")
[ "$agent_working_directory" = "$destination" ] ||
    fail "LaunchAgent WorkingDirectory '$agent_working_directory' does not match '$destination'."
launchctl print "$agent_target" >/dev/null 2>&1 ||
    fail "LaunchAgent is not loaded: $agent_target"
service_pid=$(launchctl print "$agent_target" |
    awk '/^[[:space:]]*pid = [0-9]+$/ { print $3; exit }')

# A directory lock prevents two publishers from updating the same installation.
lock_dir="$destination/.publish-lock"
mkdir "$lock_dir" 2>/dev/null || fail "Publication is already locked: $lock_dir"
stage_dir=
service_state=running
cleanup() {
    status=$?
    [ -z "$stage_dir" ] || rm -rf "$stage_dir"
    rmdir "$lock_dir" 2>/dev/null || :
    case "$service_state" in
        stopped)
            printf '%s\n' "Publication failed while $agent_label was stopped." \
                "Restore a consistent deployment, then run:" \
                "launchctl bootstrap $agent_domain $agent_plist" >&2
            ;;
        starting)
            printf '%s\n' "Publication completed, but $agent_label did not pass its health check." \
                "Inspect it with: launchctl print $agent_target" >&2
            ;;
    esac
    exit "$status"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
stage_dir=$(mktemp -d "${TMPDIR:-/tmp}/tollkar-publish.XXXXXX")

printf '%s\n' "Building web application; deployment directory: $destination"
cd "$repo_dir"
sh "$repo_dir/.codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh" \
    --label publish-web -- dotnet publish src/Tollkar.Web/Tollkar.Web.csproj \
    --configuration Release --no-self-contained --nologo --verbosity minimal --output "$stage_dir"

# Keep the current version available during the build, then stop it before replacing files.
printf '%s\n' "Stopping LaunchAgent: $agent_target"
launchctl bootout "$agent_target"
service_state=stopped
stop_deadline=$(($(date +%s) + 30))
while [ -n "$service_pid" ] && kill -0 "$service_pid" 2>/dev/null; do
    [ "$(date +%s)" -lt "$stop_deadline" ] ||
        fail "LaunchAgent process $service_pid did not stop within 30 seconds."
    sleep 1
done
launchctl print "$agent_target" >/dev/null 2>&1 &&
    fail "LaunchAgent is still loaded after bootout: $agent_target"

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

printf '%s\n' "Starting LaunchAgent: $agent_target"
launchctl bootstrap "$agent_domain" "$agent_plist"
service_state=starting
launchctl kickstart -k "$agent_target"

health_deadline=$(($(date +%s) + 30))
while :; do
    if curl --connect-timeout 1 --fail --max-time 1 --silent --output /dev/null "$health_url"; then
        service_state=running
        break
    fi
    [ "$(date +%s)" -lt "$health_deadline" ] || break
    sleep 1
done
[ "$service_state" = running ] || fail "Health check failed after 30 seconds: $health_url"

printf '%s\n' "Published, migrated and restarted successfully: $destination" \
    "Health check passed: $health_url"
