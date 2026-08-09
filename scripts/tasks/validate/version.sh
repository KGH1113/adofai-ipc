#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

VERSION="$(node -e 'const fs=require("fs");const value=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));process.stdout.write(String(value.Version));' "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json")"
if ! [[ "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-([0-9A-Za-z-]+\.)*[0-9A-Za-z-]+)?$ ]]; then
  fail "Info.json Version must be canonical SemVer without v or build metadata: $VERSION"
fi

json_value() {
  node -e 'const fs=require("fs");const value=JSON.parse(fs.readFileSync(process.argv[1],"utf8"));process.stdout.write(String(value[process.argv[2]]));' "$1" "$2"
}

require_version() {
  [ "$2" = "$VERSION" ] || fail "$1 version mismatch: expected $VERSION, got $2"
}

require_version "packages/client/package.json" "$(json_value "$ADOFAIIPC_PROJECT_ROOT/packages/client/package.json" version)"
[ -z "${GITHUB_REF_NAME:-}" ] || require_version "Git tag" "${GITHUB_REF_NAME#v}"
[ -z "${RELEASE_TAG:-}" ] || require_version "Release tag" "${RELEASE_TAG#v}"

ZIP_PATH="${1:-}"
if [ -n "$ZIP_PATH" ]; then
  require_file "$ZIP_PATH"
  zip_version="$(unzip -p "$ZIP_PATH" AdofaiIpc/Info.json | node -e 'let input="";process.stdin.on("data",chunk=>input+=chunk);process.stdin.on("end",()=>process.stdout.write(JSON.parse(input).Version));')"
  require_version "ZIP Info.json" "$zip_version"
  layout="$(unzip -Z1 "$ZIP_PATH")"
  for file in AdofaiIpc/Info.json AdofaiIpc/AdofaiIpc.Shim.dll \
    "AdofaiIpc/Launcher/versions/$VERSION/AdofaiIpc.Launcher.dll" \
    "AdofaiIpc/Runtime/versions/$VERSION/AdofaiIpc.dll" \
    AdofaiIpc/Update/state.json AdofaiIpc/AdofaiIpc.Bootstrap.dll \
    AdofaiIpc/AdofaiIpc.DependencyShim.dll AdofaiIpc/AdofaiIpc.Migration.dll; do
    grep -Fx "$file" <<< "$layout" >/dev/null || fail "ZIP is missing $file"
  done
fi

printf 'Validated AdofaiIPC product version %s.\n' "$VERSION"
