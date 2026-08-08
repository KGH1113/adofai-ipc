#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
TASKS_DIR="$SCRIPTS_DIR/tasks"
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate product version" "$TASKS_DIR/validate/version.sh"
run_task "Build npm client" "$TASKS_DIR/build/client.sh"
run_task "Check and test npm client" "$TASKS_DIR/test/client.sh"
