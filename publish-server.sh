#!/bin/sh

# Publish this repository to the local tollkar server installation.
set -eu

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd -P)
exec "$repo_dir/publish.sh" /Volumes/tollmini/_server/tollkar
