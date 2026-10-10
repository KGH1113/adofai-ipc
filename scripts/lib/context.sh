#!/usr/bin/env bash

if [ "${ADOFAIIPC_CONTEXT_LOADED:-0}" = "1" ]; then return 0; fi
ADOFAIIPC_CONTEXT_LOADED=1

ADOFAIIPC_LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADOFAIIPC_PROJECT_ROOT="$(cd "$ADOFAIIPC_LIB_DIR/../.." && pwd)"

if [ -f "$ADOFAIIPC_PROJECT_ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  source "$ADOFAIIPC_PROJECT_ROOT/.env"
  set +a
fi

ADOFAI_DIR="${ADOFAI_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"
ADOFAI_MANAGED="${ADOFAI_MANAGED:-$ADOFAI_DIR/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed}"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
DOTNET_ROOT_ARM64="${DOTNET_ROOT_ARM64:-$DOTNET_ROOT}"
DOTNET_EXE="${DOTNET_EXE:-$DOTNET_ROOT/dotnet}"

project_path() {
  case "$1" in /*) printf '%s\n' "$1" ;; *) printf '%s/%s\n' "$ADOFAIIPC_PROJECT_ROOT" "$1" ;; esac
}

ADOFAIIPC_RUNTIME_OUTPUT="$(project_path "${ADOFAIIPC_BUILD_DIR:-build/ipc}")"
ADOFAIIPC_PACKAGE_ROOT="$(project_path "${ADOFAIIPC_PACKAGE_ROOT:-build/package}")"
ADOFAIIPC_PACKAGE_STAGE="$ADOFAIIPC_PACKAGE_ROOT/bundle"
ADOFAIIPC_PACKAGE_ZIP="$(project_path "${ADOFAIIPC_PACKAGE_ZIP:-build/AdofaiIpc-v2.zip}")"

export ADOFAIIPC_PROJECT_ROOT ADOFAI_DIR ADOFAI_MANAGED
export DOTNET_ROOT DOTNET_ROOT_ARM64 DOTNET_EXE
export ADOFAIIPC_RUNTIME_OUTPUT ADOFAIIPC_PACKAGE_ROOT ADOFAIIPC_PACKAGE_STAGE ADOFAIIPC_PACKAGE_ZIP
