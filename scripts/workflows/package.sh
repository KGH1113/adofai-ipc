#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
TASKS_DIR="$SCRIPTS_DIR/tasks"
source "$SCRIPTS_DIR/lib/context.sh"
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate package inputs" "$TASKS_DIR/validate/package-inputs.sh"
run_task "Validate product version" "$TASKS_DIR/validate/version.sh"
run_task "Build runtime (Release)" "$TASKS_DIR/build/runtime.sh" Release
run_task "Build bootstrap (Release)" "$TASKS_DIR/build/bootstrap.sh" Release
run_task "Build dependency shim (Release)" "$TASKS_DIR/build/dependency-shim.sh" Release
run_task "Run C# tests" "$TASKS_DIR/test/csharp.sh"
run_task "Stage mod package" "$TASKS_DIR/package/stage.sh"
run_task "Create mod archive" "$TASKS_DIR/package/archive.sh"
run_task "Write checksum" "$TASKS_DIR/package/checksum.sh"
run_task "Verify mod archive" "$TASKS_DIR/validate/version.sh" "$ADOFAIIPC_PACKAGE_ZIP"
