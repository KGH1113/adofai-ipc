#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
configuration="${1:-Debug}"

"$DOTNET_EXE" build "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/src/AdofaiIpc.Loader/AdofaiIpc.Loader.csproj" \
  --configuration "$configuration" -p:OutputPath="$ADOFAIIPC_RUNTIME_OUTPUT/" \
  -p:AdofaiManaged="$ADOFAI_MANAGED"
