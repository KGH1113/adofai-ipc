#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$root"
npm whoami
destination="$(mktemp -d)"
trap 'rm -rf "$destination"' EXIT
pnpm --filter adofai-ipc pack --pack-destination "$destination"
pnpm --filter @adofai-ipc/typespec pack --pack-destination "$destination"
# The first publish creates the package before its Trusted Publisher can be registered.
node scripts/tasks/release/npm-release.mjs bootstrap "$destination"
for package in adofai-ipc @adofai-ipc/typespec; do
  npm trust github "$package" --repo KGH1113/adofai-ipc --file release.yml \
    --env npm --allow-publish --yes
done
echo 'npm Trusted Publishing is ready for release.yml, environment npm.'
