#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

if [ "$#" -ne 2 ]; then
  printf 'Usage: ./scripts/run.sh publish-sdk <vSDK-version> <release-notes-file>\n' >&2
  exit 2
fi
require_command gh
require_file "$2"
require_file "$ADOFAIIPC_PACKAGE_ZIP"
require_file "$ADOFAIIPC_PACKAGE_ZIP.sha256"

# Shipped v0 launchers/installers hard-code releases/latest/download/AdofaiIpc.*.
# A new SDK release must never silently move that endpoint to a DLL-only bundle.
repo="KGH1113/adofai-ipc"
legacy_tag="$(gh api "repos/$repo/releases/latest" --jq .tag_name)"
if [ "$legacy_tag" != "v0.4.3" ]; then
  printf 'Expected legacy Latest v0.4.3; found %s. Resolve the legacy update channel first.\n' "$legacy_tag" >&2
  exit 1
fi
for asset in AdofaiIpc.zip AdofaiIpc.zip.sha256 AdofaiIpc.update.json; do
  gh api "repos/$repo/releases/latest" --jq '.assets[].name' | grep -Fx "$asset" >/dev/null || {
    printf 'Legacy Latest is missing %s.\n' "$asset" >&2
    exit 1
  }
done
version="$(node -p 'JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")).version' "$ADOFAIIPC_PROJECT_ROOT/packages/adofai-ipc/package.json")"
[ "$1" = "v$version" ] || { printf 'SDK tag must match package version v%s.\n' "$version" >&2; exit 1; }
gh release create "$1" "$ADOFAIIPC_PACKAGE_ZIP" "$ADOFAIIPC_PACKAGE_ZIP.sha256" \
  --repo "$repo" --verify-tag --latest=false --title "ADOFAI IPC SDK $version" --notes-file "$2"
actual="$(gh api "repos/$repo/releases/latest" --jq .tag_name)"
[ "$actual" = "$legacy_tag" ] || { printf 'Legacy Latest unexpectedly changed to %s.\n' "$actual" >&2; exit 1; }
printf 'SDK %s published; standalone update channel remains %s.\n' "$1" "$actual"
