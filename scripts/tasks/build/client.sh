#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
require_command npm
cd "$ADOFAIIPC_PROJECT_ROOT"
npm --prefix "$ADOFAIIPC_PROJECT_ROOT/packages/client" run build
