#!/usr/bin/env bash
set -Eeuo pipefail

sha="${1:-}"
[[ "$sha" =~ ^[0-9a-f]{40}$ ]] || { echo 'A full commit SHA is required.' >&2; exit 64; }
[[ "${2:-}" == '' || "${2:-}" == '--local-only' ]] || exit 64
bundle="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root=/home/kgh/adofai-ipc-docs
release="$root/releases/$sha"
unit_dir="$HOME/.config/systemd/user"
unit=adofai-ipc-docs.service
XDG_RUNTIME_DIR="/run/user/$(id -u)"
export XDG_RUNTIME_DIR
mkdir -p "$root/releases" "$unit_dir"
exec 9>"$root/deploy.lock"
flock -n 9 || { echo 'A documentation deployment is already running.' >&2; exit 1; }

image="adofai-ipc-docs:$sha"
[[ "$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")" == "$sha" ]]
previous=''
if [[ -L "$root/current" ]]; then
  previous="$(readlink -f "$root/current")"
elif [[ -e "$root/current" ]]; then
  echo 'Current release must be a symlink.' >&2
  exit 1
fi
if [[ -n "$previous" && "$previous" != "$root/releases/"* ]]; then
  echo 'Unexpected current release path.' >&2
  exit 1
fi
backup="$(mktemp -d "$root/.rollback.XXXXXX")"
previous_active=false
previous_enabled=false
systemctl --user is-active --quiet "$unit" && previous_active=true
systemctl --user is-enabled --quiet "$unit" && previous_enabled=true
if [[ -f "$unit_dir/$unit" ]]; then cp -p "$unit_dir/$unit" "$backup/service"; fi
switched=false

compose() {
  docker compose --project-name adofai-ipc-docs --env-file "$1/compose.env" -f "$1/compose.yml" "${@:2}"
}
point_current() {
  ln -s "$1" "$root/current.next.$$"
  mv -Tf "$root/current.next.$$" "$root/current"
}
rollback() {
  local status=$?
  trap - ERR
  set +e
  if [[ "$switched" == true ]]; then
    if [[ -n "$previous" && -f "$previous/compose.env" ]]; then
      point_current "$previous"
      if [[ "$previous_active" == true ]]; then
        compose "$previous" up -d --no-build --wait --remove-orphans
      else
        compose "$release" down
      fi
    else
      compose "$release" down
      rm -f "$root/current"
    fi
    if [[ -f "$backup/service" ]]; then
      cp -p "$backup/service" "$unit_dir/$unit"
    else
      systemctl --user stop "$unit"
      rm -f "$unit_dir/$unit"
    fi
    systemctl --user daemon-reload
    if [[ "$previous_enabled" == true ]]; then systemctl --user enable "$unit"; else systemctl --user disable "$unit"; fi
    if [[ "$previous_active" == true ]]; then systemctl --user start "$unit"; fi
  fi
  rm -rf "$backup"
  echo 'Documentation deployment failed; previous deployment restored best-effort.' >&2
  exit "$status"
}
trap rollback ERR

mkdir -p "$release"
for file in compose.yml adofai-ipc-docs.service; do
  if [[ -f "$release/$file" ]]; then
    cmp "$bundle/$file" "$release/$file"
  else
    install -m 0644 "$bundle/$file" "$release/$file"
  fi
done
printf 'BUILD_SHA=%s\n' "$sha" > "$release/compose.env"
compose "$release" config --quiet
switched=true
point_current "$release"
compose "$release" up -d --no-build --wait --remove-orphans

verify() {
  local url="$1" ready=false
  for attempt in {1..15}; do
    if curl --fail --silent --show-error --max-time 10 "$url" |
      python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["gitSHA"]==sys.argv[1] and d["environment"]=="main"' "$sha"; then
      ready=true
      break
    fi
    echo "Waiting for deployment metadata ($attempt/15): $url" >&2
    sleep 2
  done
  [[ "$ready" == true ]]
}
verify http://127.0.0.1:4180/docs/deployment.json
install -m 0644 "$release/$unit" "$unit_dir/$unit"
systemctl --user daemon-reload
systemctl --user enable "$unit"
systemctl --user start "$unit"
if [[ "${2:-}" != '--local-only' ]]; then
  verify https://adofai-ipc.impl1113.dev/docs/deployment.json
fi
trap - ERR
rm -rf "$backup"
printf 'Documentation deployed: %s\n' "$sha"
compose "$release" ps
