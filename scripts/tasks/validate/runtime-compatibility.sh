#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

for assembly in \
  "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll" \
  "$ADOFAIIPC_RUNTIME_SHIM_OUTPUT/AdofaiIpc.Shim.dll" \
  "$ADOFAIIPC_LAUNCHER_OUTPUT/AdofaiIpc.Launcher.dll" \
  "$ADOFAIIPC_BOOTSTRAP_OUTPUT/AdofaiIpc.Bootstrap.dll" \
  "$ADOFAIIPC_SHIM_OUTPUT/AdofaiIpc.DependencyShim.dll" \
  "$ADOFAIIPC_MIGRATION_OUTPUT/AdofaiIpc.Migration.dll"; do
  require_file "$assembly"
  if LC_ALL=C grep -aFq 'DefaultInterpolatedStringHandler' "$assembly"; then
    fail "Unity-incompatible interpolated string handler reference in $assembly"
  fi
done

if LC_ALL=C grep -aFq '0Harmony' "$ADOFAIIPC_RUNTIME_OUTPUT/AdofaiIpc.dll"; then
  fail "AdofaiIpc runtime unexpectedly references 0Harmony"
fi

printf 'Validated shipped assemblies do not reference the Harmony string handler.\n'
