#!/bin/sh

# Publish separately from persistent server data and restart its launchd service.
set -eu
umask 077

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd -P)
. "$repo_dir/publish-common.sh"

agent_label=local.tollkar.web
health_url=http://127.0.0.1:5080/api/health

[ "$#" -eq 1 ] && [ -n "$1" ] || publication_fail "Usage: $0 /absolute/or/relative/deployment-directory"
require_publication_tools awk curl date dotnet launchctl npm plutil rsync
resolve_publication_destination "$1"

agent_plist=${HOME:?}/Library/LaunchAgents/$agent_label.plist
agent_domain=gui/$(id -u)
agent_target=$agent_domain/$agent_label
[ -f "$agent_plist" ] || publication_fail "LaunchAgent not found: $agent_plist"
[ "$(plutil -extract Label raw -o - "$agent_plist")" = "$agent_label" ] ||
    publication_fail "LaunchAgent label must be $agent_label: $agent_plist"
agent_working_directory=$(plutil -extract WorkingDirectory raw -o - "$agent_plist")
[ "$agent_working_directory" = "$destination" ] ||
    publication_fail "LaunchAgent WorkingDirectory '$agent_working_directory' does not match '$destination'."

# A directory lock prevents two publishers from updating the same installation.
lock_dir=
stage_dir=
service_state=unchanged
cleanup() {
    status=$?
    cleanup_publication_artifacts
    case "$service_state" in
        stopped)
            printf '%s\n' "Publication failed while $agent_label was stopped." \
                "Restore a consistent deployment, then run:" \
                "launchctl bootstrap $agent_domain $agent_plist" >&2
            ;;
        starting)
            printf '%s\n' "Publication updated files, but $agent_label did not start or pass its health check." \
                "Inspect it with: launchctl print $agent_target" >&2
            ;;
    esac
    exit "$status"
}
trap cleanup 0
trap 'exit 1' HUP INT TERM

acquire_publication_lock
create_publication_stage
build_web_publication

# Keep the current version available during the build, then stop it before replacing files.
service_pid=
if launchctl print "$agent_target" >/dev/null 2>&1; then
    service_pid=$(launchctl print "$agent_target" |
        awk '/^[[:space:]]*pid = [0-9]+$/ { print $3; exit }')
    printf '%s\n' "Stopping LaunchAgent: $agent_target"
    launchctl bootout "$agent_target"
    printf '%s\n' "LaunchAgent stopped: $agent_target"
else
    printf '%s\n' "LaunchAgent is already stopped: $agent_target"
fi
service_state=stopped
stop_deadline=$(($(date +%s) + 30))
while [ -n "$service_pid" ] && kill -0 "$service_pid" 2>/dev/null; do
    [ "$(date +%s)" -lt "$stop_deadline" ] ||
        publication_fail "LaunchAgent process $service_pid did not stop within 30 seconds."
    sleep 1
done
launchctl print "$agent_target" >/dev/null 2>&1 &&
    publication_fail "LaunchAgent is still loaded before copying files: $agent_target"

# Never delete destination files or copy development databases, media or logs.
# Existing server configuration wins; new configuration files are seeded once.
copy_web_publication
# Relative SQLite paths must resolve exactly as they do for the deployed server.
# Explicit migration mode never starts hosted services or scans the song directory.
cd "$destination"
printf '%s\n' "Migrating databases in: $destination"
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
[ "$service_state" = running ] || publication_fail "Health check failed after 30 seconds: $health_url"

printf '%s\n' "Published, migrated and restarted successfully: $destination" \
    "Health check passed: $health_url"
