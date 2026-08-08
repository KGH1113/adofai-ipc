#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"

require_command shasum
require_file "$ADOFAIIPC_PACKAGE_ZIP"
checksum="$ADOFAIIPC_PACKAGE_ZIP.sha256"
(
  cd "$(dirname "$ADOFAIIPC_PACKAGE_ZIP")"
  shasum -a 256 "$(basename "$ADOFAIIPC_PACKAGE_ZIP")" > "$(basename "$checksum")"
)
printf 'Checksum written to %s\n' "$checksum"
