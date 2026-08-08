#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate shell scripts" "$SCRIPTS_DIR/tasks/validate/shell-scripts.sh"
run_task "Validate product version" "$SCRIPTS_DIR/tasks/validate/version.sh"
