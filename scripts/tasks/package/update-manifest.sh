#!/usr/bin/env bash
set -euo pipefail
TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
source "$TASK_DIR/../../lib/guards.sh"
require_file "$ADOFAIIPC_PACKAGE_ZIP"
version="$(node -e 'const fs=require("fs");process.stdout.write(JSON.parse(fs.readFileSync(process.argv[1],"utf8")).Version)' "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc/Info.json")"
size="$(stat -f %z "$ADOFAIIPC_PACKAGE_ZIP" 2>/dev/null || stat -c %s "$ADOFAIIPC_PACKAGE_ZIP")"
sha="$(shasum -a 256 "$ADOFAIIPC_PACKAGE_ZIP" | awk '{print $1}')"
manifest="$(dirname "$ADOFAIIPC_PACKAGE_ZIP")/AdofaiIpc.update.json"
printf '{\n  "SchemaVersion": 1,\n  "Version": "%s",\n  "PackageUrl": "https://github.com/KGH1113/adofai-ipc/releases/download/v%s/AdofaiIpc.zip",\n  "PackageSize": %s,\n  "Sha256": "%s"\n}\n' "$version" "$version" "$size" "$sha" > "$manifest"
printf 'Update manifest written to %s\n' "$manifest"
