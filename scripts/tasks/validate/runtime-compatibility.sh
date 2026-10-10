#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

for assembly in AdofaiIpc.Contracts AdofaiIpc.Loader AdofaiIpc.Runtime; do
  require_file "$ADOFAIIPC_RUNTIME_OUTPUT/$assembly.dll"
  if LC_ALL=C grep -aFq 'DefaultInterpolatedStringHandler' "$ADOFAIIPC_RUNTIME_OUTPUT/$assembly.dll"; then
    fail "Unity-incompatible interpolated string handler reference in $assembly"
  fi
done
for dependency in 0Harmony UnityModManager JALib; do
  if LC_ALL=C grep -aFq "$dependency" "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.Runtime.dll"; then
    fail "IPC runtime unexpectedly references $dependency"
  fi
done
printf 'Validated Unity-compatible DLLs without mod-loader dependencies.\n'
