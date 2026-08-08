#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
source "$TASK_DIR/../../lib/artifacts.sh"

assert_non_root_path "$ADOFAIIPC_INSTALL_PATH"
mkdir -p "$ADOFAIIPC_INSTALL_PATH"
if [ -e "$ADOFAIIPC_INSTALL_PATH/assembly_cache" ]; then
  safe_remove_tree "$ADOFAIIPC_INSTALL_PATH/assembly_cache" "$ADOFAIIPC_INSTALL_PATH"
fi
rm -f "$ADOFAIIPC_INSTALL_PATH/JAModInfo.json" "$ADOFAIIPC_INSTALL_PATH/JAMod.Bootstrap.dll"
rm -f "$ADOFAIIPC_INSTALL_PATH"/JAMod.Bootstrap.dll.*.cache
copy_mod_artifacts "$ADOFAIIPC_INSTALL_PATH"
copy_debug_symbols "$ADOFAIIPC_INSTALL_PATH"
printf 'Installed to %s\n' "$ADOFAIIPC_INSTALL_PATH"
