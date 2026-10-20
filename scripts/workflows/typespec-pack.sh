#!/usr/bin/env bash
set -euo pipefail

WORKFLOW_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPTS_DIR="$(cd "$WORKFLOW_DIR/.." && pwd)"
source "$SCRIPTS_DIR/lib/context.sh"
source "$SCRIPTS_DIR/lib/logging.sh"

run_task "Validate product version" "$SCRIPTS_DIR/tasks/validate/version.sh"
cd "$ADOFAIIPC_PROJECT_ROOT"
pnpm --filter @adofai-ipc/typespec build
pnpm --filter @adofai-ipc/typespec check
pnpm --filter @adofai-ipc/typespec test
destination="${ADOFAIIPC_CLIENT_PACK_DESTINATION:-$ADOFAIIPC_PROJECT_ROOT/build}"
mkdir -p "$destination"
pnpm --filter @adofai-ipc/typespec pack --pack-destination "$destination"
