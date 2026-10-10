#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

node "$TASK_DIR/version.mjs" "$ADOFAIIPC_PROJECT_ROOT"
ZIP_PATH="${1:-}"
if [ -n "$ZIP_PATH" ]; then
  require_file "$ZIP_PATH"
  layout="$(unzip -Z1 "$ZIP_PATH")"
  for file in ipc/manifest.json AdofaiIpc.Contracts.dll AdofaiIpc.Loader.dll ipc/AdofaiIpc.Runtime.dll; do
    grep -Fx "$file" <<< "$layout" >/dev/null || fail "ZIP is missing $file"
  done
  [ "$(unzip -p "$ZIP_PATH" ipc/manifest.json)" = "$(cat "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/manifest.json")" ] || fail "ZIP manifest differs from source"
  if grep -E 'Info\.json|Bootstrap|Shim|Launcher|Migration|Update/' <<< "$layout" >/dev/null; then
    fail "ZIP contains obsolete common-mod installation artifacts"
  fi
fi
