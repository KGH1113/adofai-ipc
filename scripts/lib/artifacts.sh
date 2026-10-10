#!/usr/bin/env bash

if [ "${ADOFAIIPC_ARTIFACTS_LOADED:-0}" = "1" ]; then return 0; fi
ADOFAIIPC_ARTIFACTS_LOADED=1
ADOFAIIPC_ARTIFACTS_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$ADOFAIIPC_ARTIFACTS_LIB_DIR/context.sh"
source "$ADOFAIIPC_ARTIFACTS_LIB_DIR/guards.sh"

copy_bundle_artifacts() {
  local destination="$1"
  local assembly
  mkdir -p "$destination/ipc"
  for assembly in AdofaiIpc.Contracts AdofaiIpc.Loader; do
    require_file "$ADOFAIIPC_RUNTIME_OUTPUT/$assembly.dll"
    cp "$ADOFAIIPC_RUNTIME_OUTPUT/$assembly.dll" "$destination/"
  done
  require_file "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.Runtime.dll"
  cp "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.Runtime.dll" "$destination/ipc/"
  cp "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/manifest.json" "$destination/ipc/"
}
