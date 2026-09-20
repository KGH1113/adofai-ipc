#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
TASKS_DIR="$SCRIPTS_DIR/tasks"
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate local build inputs" "$TASKS_DIR/validate/local-build-inputs.sh"
run_task "Validate product version" "$TASKS_DIR/validate/version.sh"
run_task "Build runtime (Debug)" "$TASKS_DIR/build/runtime.sh" Debug
run_task "Build runtime shim (Debug)" "$TASKS_DIR/build/runtime-shim.sh" Debug
run_task "Build launcher (Debug)" "$TASKS_DIR/build/launcher.sh" Debug
run_task "Build bootstrap (Debug)" "$TASKS_DIR/build/bootstrap.sh" Debug
run_task "Build dependency shim (Debug)" "$TASKS_DIR/build/dependency-shim.sh" Debug
run_task "Build migration support (Debug)" "$TASKS_DIR/build/migration.sh" Debug
run_task "Validate Unity runtime compatibility" "$TASKS_DIR/validate/runtime-compatibility.sh"
run_task "Run C# tests" "$TASKS_DIR/test/csharp.sh"

if [ "${ADOFAIIPC_SKIP_INSTALL:-0}" = "1" ]; then
  log_skip "Install mod (ADOFAIIPC_SKIP_INSTALL=1)"
else
  run_task "Install mod" "$TASKS_DIR/install/mod.sh"
fi
