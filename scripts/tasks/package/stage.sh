#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
source "$TASK_DIR/../../lib/artifacts.sh"

assert_non_root_path "$ADOFAIIPC_PACKAGE_ROOT"
assert_non_root_path "$ADOFAIIPC_PACKAGE_STAGE"
mkdir -p "$ADOFAIIPC_PACKAGE_ROOT"
if [ -e "$ADOFAIIPC_PACKAGE_STAGE" ]; then
  safe_remove_tree "$ADOFAIIPC_PACKAGE_STAGE" "$ADOFAIIPC_PACKAGE_ROOT"
fi
mkdir -p "$ADOFAIIPC_PACKAGE_STAGE"
copy_mod_artifacts "$ADOFAIIPC_PACKAGE_STAGE"
copy_debug_symbols "$ADOFAIIPC_PACKAGE_STAGE"
