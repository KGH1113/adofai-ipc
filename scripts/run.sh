#!/usr/bin/env bash
set -euo pipefail

SCRIPTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/logging.sh
source "$SCRIPTS_DIR/lib/logging.sh"

usage() {
  cat <<'USAGE'
Usage: ./scripts/run.sh <command>

Commands:
  build    Build and test the bundled DLLs (no game installation)
  package  Build and verify the DLL bundle ZIP and checksum
  client   Check the TypeScript SDK, generator and C# interoperability
  client-sync  Alias for sdk-sync
  sdk-sync  Export the typed browser SDK with its transport bundled
  client-pack  Check and pack the SDK to ADOFAIIPC_CLIENT_PACK_DESTINATION
  typespec-pack  Check and pack the TypeSpec generator
  check    Validate shell scripts, version metadata and architecture boundaries
  help     Show this help
USAGE
}

case "${1:-help}" in
  build) exec "$SCRIPTS_DIR/workflows/build.sh" ;;
  package) exec "$SCRIPTS_DIR/workflows/package.sh" ;;
  client) exec "$SCRIPTS_DIR/workflows/client.sh" ;;
  client-sync) shift; exec node "$SCRIPTS_DIR/tasks/client/sync-source.mjs" "$@" ;;
  sdk-sync) shift; exec node "$SCRIPTS_DIR/tasks/client/export-sdk.mjs" "$@" ;;
  client-pack) exec "$SCRIPTS_DIR/workflows/client-pack.sh" ;;
  typespec-pack) exec bash "$SCRIPTS_DIR/workflows/typespec-pack.sh" ;;
  check) exec "$SCRIPTS_DIR/workflows/check-scripts.sh" ;;
  help|-h|--help) usage ;;
  *)
    printf 'Unknown command: %s\n\n' "$1" >&2
    usage >&2
    exit 2
    ;;
esac
