#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
configuration="${1:-Debug}"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" build "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.DependencyShim/AdofaiIpc.DependencyShim.csproj" \
    --configuration "$configuration" \
    -p:OutputPath="$ADOFAIIPC_SHIM_OUTPUT/" \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"
