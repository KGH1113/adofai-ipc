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
  require_file "$ADOFAIIPC_RUNTIME_SHIM_OUTPUT/AdofaiIpc.Shim.dll"
  require_file "$ADOFAIIPC_LAUNCHER_OUTPUT/AdofaiIpc.Launcher.dll"
  require_file "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.dll"
  require_file "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.dll"
  require_file "$ADOFAIIPC_MIGRATION_OUTPUT/AdofaiIpc.Migration.dll"
  mkdir -p "$destination"
  cp "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json" "$destination/"
  local version
  version="$(node -e 'const fs=require("fs");process.stdout.write(JSON.parse(fs.readFileSync(process.argv[1],"utf8")).Version)' "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json")"
  mkdir -p "$destination/Update" "$destination/Launcher/versions/$version" "$destination/Runtime/versions/$version"
  cp "$ADOFAIIPC_RUNTIME_SHIM_OUTPUT/AdofaiIpc.Shim.dll" "$destination/"
  # Official bootstrap 0.2.0 accepts packages only when a root runtime DLL exists.
  # Info.json still points at the fixed shim, so this compatibility copy is never loaded.
  cp "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll" "$destination/"
  cp "$ADOFAIIPC_LAUNCHER_OUTPUT/AdofaiIpc.Launcher.dll" "$destination/Launcher/versions/$version/"
  cp "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll" "$destination/Runtime/versions/$version/"
  printf '{\n  "SchemaVersion": 1,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null\n}\n' "$version" > "$destination/Update/state.json"
  cp "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.dll" "$destination/"
  cp "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.dll" "$destination/"
  cp "$ADOFAIIPC_MIGRATION_OUTPUT/AdofaiIpc.Migration.dll" "$destination/"
}

copy_debug_symbols() {
  local destination="$1"
  local symbol
  for symbol in \
    "$ADOFAIIPC_RUNTIME_SHIM_OUTPUT/AdofaiIpc.Shim.pdb" \
    "$ADOFAIIPC_LAUNCHER_OUTPUT/AdofaiIpc.Launcher.pdb" \
    "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.pdb" \
    "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.pdb" \
    "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.pdb" \
    "$ADOFAIIPC_MIGRATION_OUTPUT/AdofaiIpc.Migration.pdb"; do
    [ ! -f "$symbol" ] || cp "$symbol" "$destination/"
  done
}
