#!/usr/bin/env bash
set -euo pipefail
TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
test_editor="${ADOFAIIPC_TEST_UNITY:-/Applications/Unity/Hub/Editor/6000.3.10f1/Unity.app/Contents/MacOS/Unity}"
require_file "$test_editor"
test_project="$ADOFAIIPC_PROJECT_ROOT/build/UnityUpgradeSmoke"
mkdir -p "$test_project"
cp -R "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.MonoTests/UnitySmoke/." "$test_project/"
version="$(node -p 'JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")).Version' "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json")"
for old in 0.4.1 0.4.2; do
  baseline="$ADOFAIIPC_PROJECT_ROOT/build/mono-upgrade/published-$old/extracted/AdofaiIpc"
  require_file "$baseline/Info.json"
  IPC_SMOKE_OLD_ROOT="$baseline" IPC_SMOKE_OLD_VERSION="$old" \
    IPC_SMOKE_CANDIDATE="$ADOFAIIPC_PACKAGE_STAGE" IPC_SMOKE_MANAGED="$ADOFAI_MANAGED" \
    IPC_SMOKE_VERSION="$version" IPC_SMOKE_RESULT="$test_project/result-$old.json" \
    "$test_editor" -batchmode -nographics -projectPath "$test_project" \
      -executeMethod UpgradeSmoke.Start -logFile "$test_project/probe-$old.log"
  cat "$test_project/result-$old.json"
done
