#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
require_command pnpm
cd "$ADOFAIIPC_PROJECT_ROOT"
pnpm --filter @adofai-ipc/typespec check
pnpm --filter adofai-ipc build
pnpm --filter adofai-ipc check
pnpm --filter @adofai-ipc/typespec test
ADOFAIIPC_PROTOCOL_FIXTURE="$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Protocol.Tests/bin/Debug/net8.0/AdofaiIpc.Protocol.Tests.dll" \
  DOTNET_EXE="$DOTNET_EXE" pnpm --filter adofai-ipc test
