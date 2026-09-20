#!/usr/bin/env bash
set -euo pipefail

SCRIPTS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/logging.sh
source "$SCRIPTS_DIR/lib/logging.sh"

usage() {
  cat <<'USAGE'
Usage: ./scripts/run.sh <command>

Commands:
  build    Build, test, and optionally install the mod
  package  Build and verify the mod ZIP and checksum
  client   Check and test the npm client
  client-pack  Check and pack the npm client to ADOFAIIPC_CLIENT_PACK_DESTINATION
  check    Validate all shell scripts and version metadata
  help     Show this help
USAGE
}

case "${1:-help}" in
  build) exec "$SCRIPTS_DIR/workflows/build-install.sh" ;;
  package) exec "$SCRIPTS_DIR/workflows/package.sh" ;;
  client) exec "$SCRIPTS_DIR/workflows/client.sh" ;;
  client-pack) exec "$SCRIPTS_DIR/workflows/client-pack.sh" ;;
  check) exec "$SCRIPTS_DIR/workflows/check-scripts.sh" ;;
  help|-h|--help) usage ;;
  *)
    printf 'Unknown command: %s\n\n' "$1" >&2
    usage >&2
    exit 2
    ;;
esac
