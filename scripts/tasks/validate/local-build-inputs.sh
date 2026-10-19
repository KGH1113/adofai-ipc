#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../../lib/context.sh
source "$TASK_DIR/../../lib/context.sh"
# shellcheck source=../../lib/guards.sh
source "$TASK_DIR/../../lib/guards.sh"

require_file "$DOTNET_EXE"
require_dir "$ADOFAI_MANAGED"
require_file "$ADOFAI_MANAGED/UnityEngine.CoreModule.dll"
require_file "$ADOFAI_MANAGED/Newtonsoft.Json.dll"
require_file "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/src/AdofaiIpc.Runtime/manifest.json"
require_command node
