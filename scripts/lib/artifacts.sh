#!/usr/bin/env bash

if [ "${ADOFAIIPC_ARTIFACTS_LOADED:-0}" = "1" ]; then return 0; fi
ADOFAIIPC_ARTIFACTS_LOADED=1

ADOFAIIPC_ARTIFACTS_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=context.sh
source "$ADOFAIIPC_ARTIFACTS_LIB_DIR/context.sh"
# shellcheck source=guards.sh
source "$ADOFAIIPC_ARTIFACTS_LIB_DIR/guards.sh"

copy_mod_artifacts() {
  local destination="$1"
  require_file "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json"
  require_file "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll"
  require_file "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.dll"
  require_file "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.dll"
  mkdir -p "$destination"
  cp "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json" "$destination/"
  cp "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll" "$destination/"
  cp "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.dll" "$destination/"
  cp "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.dll" "$destination/"
}

copy_debug_symbols() {
  local destination="$1"
  local symbol
  for symbol in \
    "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.pdb" \
    "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.pdb" \
    "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.pdb"; do
    [ ! -f "$symbol" ] || cp "$symbol" "$destination/"
  done
}
