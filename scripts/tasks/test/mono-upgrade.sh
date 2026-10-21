#!/usr/bin/env bash
set -euo pipefail
TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
require_command curl
require_command unzip
require_command shasum
require_command mcs
test_mono="${ADOFAIIPC_TEST_MONO:-}"
if [ -z "$test_mono" ]; then
  for candidate in /Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge/bin/mono; do
    if [ -x "$candidate" ]; then test_mono="$candidate"; break; fi
  done
fi
test_mono="${test_mono:-$(command -v mono || true)}"
[ -n "$test_mono" ] || fail "Set ADOFAIIPC_TEST_MONO to a Mono CLI (prefer Unity Mono)."
test_frameworks="${ADOFAI_MANAGED%/Contents/*}/Contents/Frameworks"
require_file "$ADOFAIIPC_PACKAGE_ZIP"
version="$(node -p 'JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")).Version' "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json")"
evidence="$ADOFAIIPC_PROJECT_ROOT/build/mono-upgrade"
mkdir -p "$evidence"
mcs -out:"$evidence/AssemblyUpgrade.exe" "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.MonoTests/AssemblyUpgrade.cs"
for old in 0.4.1 0.4.2; do
  baseline="$evidence/published-$old"
  mkdir -p "$baseline"
  for name in AdofaiIpc.zip AdofaiIpc.zip.sha256; do
    if [ ! -f "$baseline/$name" ]; then
      curl -fsSL --retry 2 --connect-timeout 10 --max-time 120 \
        "https://github.com/KGH1113/adofai-ipc/releases/download/v$old/$name" -o "$baseline/$name.part"
      mv "$baseline/$name.part" "$baseline/$name"
    fi
  done
  (cd "$baseline" && shasum -a 256 -c AdofaiIpc.zip.sha256)
  unzip -q -o "$baseline/AdofaiIpc.zip" -d "$baseline/extracted"
  DYLD_LIBRARY_PATH="$test_frameworks${DYLD_LIBRARY_PATH:+:$DYLD_LIBRARY_PATH}" \
    "$test_mono" "$evidence/AssemblyUpgrade.exe" "$baseline/extracted/AdofaiIpc" "$old" \
    "$ADOFAIIPC_PACKAGE_ZIP" "$ADOFAI_MANAGED" "$version"
done
