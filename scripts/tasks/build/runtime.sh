#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
configuration="${1:-Debug}"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
  "$DOTNET_EXE" build "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/AdofaiIpc.csproj" \
    --configuration "$configuration" \
    -p:OutputPath="$ADOFAIIPC_RUNTIME_OUTPUT/" \
    -p:AdofaiManaged="$ADOFAI_MANAGED" \
    -p:AdofaiMods="$ADOFAI_MODS_DIR" \
    -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL" \
    -p:HarmonyDll="$HARMONY_DLL"
