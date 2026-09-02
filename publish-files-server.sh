#!/bin/sh

# Copy a fresh build to the local server without restarting it or migrating databases.
set -eu

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd -P)
exec "$repo_dir/publish-files.sh" /Volumes/tollmini/_server/tollkar
