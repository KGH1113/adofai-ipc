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
SHIM_OUT="$(project_path "${ADOFAIIPC_SHIM_BUILD_DIR:-build/AdofaiIpc.Shim}")"
LAUNCHER_OUT="$(project_path "${ADOFAIIPC_LAUNCHER_BUILD_DIR:-build/AdofaiIpc.Launcher}")"
DEST="$(project_path "${ADOFAIIPC_INSTALL_DIR:-$ADOFAI_MODS_DIR/AdofaiIpc}")"
VERSION="0.3.0"

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

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
"$DOTNET_EXE" build "$PROJECT/AdofaiIpc.Shim/AdofaiIpc.Shim.csproj" \
  -p:OutputPath="$SHIM_OUT/" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" \
  -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
"$DOTNET_EXE" build "$PROJECT/AdofaiIpc.Launcher/AdofaiIpc.Launcher.csproj" \
  -p:OutputPath="$LAUNCHER_OUT/" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" \
  -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"

DOTNET_ROOT="$DOTNET_ROOT" DOTNET_ROOT_ARM64="$DOTNET_ROOT_ARM64" \
"$DOTNET_EXE" run --project "$PROJECT/AdofaiIpc.UpdateTests/AdofaiIpc.UpdateTests.csproj" \
  -p:AdofaiManaged="$ADOFAI_MANAGED" \
  -p:UnityModManagerDll="$UNITY_MOD_MANAGER_DLL"

if [ "${ADOFAIIPC_SKIP_INSTALL:-0}" = "1" ]; then
  echo "Build completed without installing (ADOFAIIPC_SKIP_INSTALL=1)."
  exit 0
fi

mkdir -p "$DEST"
rm -rf "$DEST/assembly_cache"
cp "$PROJECT/AdofaiIpc/Info.json" "$DEST/"
rm -f "$DEST/JAModInfo.json" "$DEST/JAMod.Bootstrap.dll"
rm -f "$DEST"/JAMod.Bootstrap.dll.*.cache
mkdir -p "$DEST/Launcher/versions/$VERSION" "$DEST/Runtime/versions/$VERSION" "$DEST/Update"
cp "$SHIM_OUT/AdofaiIpc.Shim.dll" "$DEST/"
cp "$LAUNCHER_OUT/AdofaiIpc.Launcher.dll" "$DEST/Launcher/versions/$VERSION/"
cp "$OUT/AdofaiIpc.dll" "$DEST/Runtime/versions/$VERSION/"
cp "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.dll" "$DEST/"
printf '{\n  "SchemaVersion": 1,\n  "Current": "%s",\n  "Previous": null,\n  "Trial": null\n}\n' \
  "$VERSION" > "$DEST/Update/state.json"

if [ -f "$OUT/AdofaiIpc.pdb" ]; then
  cp "$OUT/AdofaiIpc.pdb" "$DEST/Runtime/versions/$VERSION/"
fi

if [ -f "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.pdb" ]; then
  cp "$BOOTSTRAP_OUT/AdofaiIpc.Bootstrap.pdb" "$DEST/"
fi

if [ -f "$SHIM_OUT/AdofaiIpc.Shim.pdb" ]; then
  cp "$SHIM_OUT/AdofaiIpc.Shim.pdb" "$DEST/"
fi

if [ -f "$LAUNCHER_OUT/AdofaiIpc.Launcher.pdb" ]; then
  cp "$LAUNCHER_OUT/AdofaiIpc.Launcher.pdb" "$DEST/Launcher/versions/$VERSION/"
fi

echo "Installed to $DEST"
