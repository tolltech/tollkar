#!/bin/sh

# Run the complete repository validation used before handing changes back.
set -eu

repo_dir=$(CDPATH= cd "$(dirname "$0")" && pwd)
dotnet_quiet="$repo_dir/.codex/skills/dotnet-minimal-output/scripts/dotnet-quiet.sh"
client_dir="$repo_dir/src/Tollkar.Web/ClientApp"

sh -n "$repo_dir/publish.sh"
sh -n "$repo_dir/publish-server.sh"
sh -n "$repo_dir/publish-common.sh"
sh -n "$repo_dir/publish-files.sh"
sh -n "$repo_dir/publish-files-server.sh"
sh -n "$repo_dir/tests/publish-scripts.sh"
sh "$repo_dir/tests/publish-scripts.sh"

if [ ! -d "$client_dir/node_modules" ]; then
    (cd "$client_dir" && npm ci)
fi

(cd "$client_dir" && npm run lint && npm test && npm run build)

sh "$dotnet_quiet" --label build-solution -- dotnet build "$repo_dir/Tollkar.sln" \
    --nologo --verbosity minimal --disable-build-servers --maxcpucount:1
sh "$dotnet_quiet" --label test-solution -- dotnet test "$repo_dir/Tollkar.sln" \
    --nologo --verbosity minimal --no-build --disable-build-servers --maxcpucount:1
