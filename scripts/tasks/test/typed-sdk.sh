#!/usr/bin/env bash
set -euo pipefail
TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
require_command pnpm
cd "$ADOFAIIPC_PROJECT_ROOT"
pnpm --filter @adofai-ipc/typespec build
pnpm --filter @adofai-ipc/typespec check
pnpm --filter @adofai-ipc/typespec test
DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64"   "$DOTNET_EXE" build "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/tests/AdofaiIpc.Protocol.Tests/AdofaiIpc.Protocol.Tests.csproj"   -p:AdofaiManaged="$ADOFAI_MANAGED" --verbosity quiet
ADOFAIIPC_PROTOCOL_FIXTURE="$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/tests/AdofaiIpc.Protocol.Tests/bin/Debug/net8.0/AdofaiIpc.Protocol.Tests.dll"   DOTNET_EXE="$DOTNET_EXE" node --test --test-concurrency=1 tests/interoperability/*.test.mjs
