#!/usr/bin/env bash
set -euo pipefail

TASK_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$TASK_DIR/../../lib/context.sh"
FIXTURE_OUTPUT="$ADOFAIIPC_PROJECT_ROOT/build/loader-fixtures"

for version in 2.0.0 2.1.0; do
  "$DOTNET_EXE" build "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Loader.Tests/Fixtures/Runtime/Runtime.csproj" \
    -p:ProductVersion="$version" -p:OutputPath="$FIXTURE_OUTPUT/$version/"
done
"$DOTNET_EXE" run --project "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Loader.Tests/AdofaiIpc.Loader.Tests.csproj" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" -- "$FIXTURE_OUTPUT"
"$DOTNET_EXE" run --project "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Download.Tests/AdofaiIpc.Download.Tests.csproj"
"$DOTNET_EXE" run --project "$ADOFAIIPC_PROJECT_ROOT/AdofaiIpc.Protocol.Tests/AdofaiIpc.Protocol.Tests.csproj" \
  -p:AdofaiManaged="$ADOFAI_MANAGED"
