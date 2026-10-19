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
  client   Check the clients, TypeSpec generator and C# integration
  client-sync  Copy raw SDK source and provenance into a consumer vendor directory
  sdk-sync  Export the typed browser SDK with its transport bundled
  client-pack  Check and pack the npm client to ADOFAIIPC_CLIENT_PACK_DESTINATION
  check    Validate all shell scripts and version metadata
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
  check) exec "$SCRIPTS_DIR/workflows/check-scripts.sh" ;;
  help|-h|--help) usage ;;
  *)
    printf 'Unknown command: %s\n\n' "$1" >&2
    usage >&2
    exit 2
    ;;
esac
