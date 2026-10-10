#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

require_command zip
require_dir "$ADOFAIIPC_PACKAGE_STAGE"
rm -f "$ADOFAIIPC_PACKAGE_ZIP"
mkdir -p "$(dirname "$ADOFAIIPC_PACKAGE_ZIP")"
(
  cd "$ADOFAIIPC_PACKAGE_STAGE"
  zip -r "$ADOFAIIPC_PACKAGE_ZIP" AdofaiIpc.Contracts.dll AdofaiIpc.Loader.dll ipc
)
printf 'Packaged to %s\n' "$ADOFAIIPC_PACKAGE_ZIP"
