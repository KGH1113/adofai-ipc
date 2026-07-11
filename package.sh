#!/usr/bin/env bash
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ -f "$PROJECT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  source "$PROJECT/.env"
  set +a
fi

ADOFAI_DIR="${ADOFAI_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"
ADOFAI_MODS_DIR="${ADOFAI_MODS_DIR:-$ADOFAI_DIR/Mods}"
ADOFAI_MANAGED="${ADOFAI_MANAGED:-$ADOFAI_DIR/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed}"

DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
DOTNET_ROOT_ARM64="${DOTNET_ROOT_ARM64:-$DOTNET_ROOT}"
DOTNET_EXE="${DOTNET_EXE:-$DOTNET_ROOT/dotnet}"

UNITY_MOD_MANAGER_DLL="${UNITY_MOD_MANAGER_DLL:-$ADOFAI_MANAGED/UnityModManager/UnityModManager.dll}"
HARMONY_DLL="${HARMONY_DLL:-$ADOFAI_MANAGED/UnityModManager/0Harmony.dll}"

project_path() {
  case "$1" in
    /*) printf '%s\n' "$1" ;;
    *) printf '%s/%s\n' "$PROJECT" "$1" ;;
  esac
}

OUT="$(project_path "${ADOFAIIPC_BUILD_DIR:-build/AdofaiIpc}")"
BOOTSTRAP_OUT="$(project_path "${ADOFAIIPC_BOOTSTRAP_BUILD_DIR:-build/AdofaiIpc.Bootstrap}")"
PACKAGE_ROOT="$(project_path "${ADOFAIIPC_PACKAGE_ROOT:-build/package}")"
STAGE="$PACKAGE_ROOT/AdofaiIpc"
ZIP_PATH="$(project_path "${ADOFAIIPC_PACKAGE_ZIP:-build/AdofaiIpc.zip}")"
CHECKSUM_PATH="$ZIP_PATH.sha256"

require_file() {
  if [ ! -f "$1" ]; then
    echo "Missing required file: $1" >&2
    exit 1
  fi
}

require_dir() {
  if [ ! -d "$1" ]; then
    echo "Missing required directory: $1" >&2
    exit 1
  fi
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

require_command zip
require_command shasum
require_file "$DOTNET_EXE"
require_dir "$ADOFAI_MANAGED"
require_file "$UNITY_MOD_MANAGER_DLL"
require_file "$HARMONY_DLL"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
"$DOTNET_EXE" build "$PROJECT/AdofaiIpc/AdofaiIpc.csproj" \
  -p:OutputPath="$OUT/" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" \
  -p:AdofaiMods="$ADOFAI_MODS_DIR" \
  -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL" \
  -p:HarmonyDll="$HARMONY_DLL"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
"$DOTNET_EXE" build "$PROJECT/AdofaiIpc.Bootstrap/AdofaiIpc.Bootstrap.csproj" \
  -p:OutputPath="$BOOTSTRAP_OUT/" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" \
  -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"

rm -rf "$STAGE"
mkdir -p "$STAGE"

cp "$PROJECT/AdofaiIpc/Info.json" "$STAGE/"
cp "$OUT/AdofaiIpc.dll" "$STAGE/"
cp "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.dll" "$STAGE/"

if [ -f "$OUT/AdofaiIpc.pdb" ]; then
  cp "$OUT/AdofaiIpc.pdb" "$STAGE/"
fi

if [ -f "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.pdb" ]; then
  cp "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.pdb" "$STAGE/"
fi

rm -f "$ZIP_PATH"
rm -f "$CHECKSUM_PATH"
mkdir -p "$(dirname "$ZIP_PATH")"
(
  cd "$PACKAGE_ROOT"
  zip -r "$ZIP_PATH" AdofaiIpc \
    -x 'AdofaiIpc/Data/*' \
    -x 'AdofaiIpc/*.log'
)

(
  cd "$(dirname "$ZIP_PATH")"
  shasum -a 256 "$(basename "$ZIP_PATH")" > "$(basename "$CHECKSUM_PATH")"
)

echo "Packaged to $ZIP_PATH"
echo "Checksum written to $CHECKSUM_PATH"
