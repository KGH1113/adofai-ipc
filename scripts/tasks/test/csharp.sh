#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" run --project "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Bootstrap.Tests/AdofaiIpc.Bootstrap.Tests.csproj" \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"
