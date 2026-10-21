#!/usr/bin/env python3
"""One-time, fixed hostname setup. Preview by default; sudo python3 ... --apply to install."""
import argparse
import datetime
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

HOSTNAME = "adofai-ipc.impl1113.dev"
NGINX = Path("/etc/nginx/sites-available/impl1113.dev")
TUNNEL = Path("/etc/cloudflared/config.yml")
BEGIN = "# BEGIN ADOFAI IPC docs"
END = "# END ADOFAI IPC docs"
BLOCK = f"""{BEGIN}
server {{
    listen 127.0.0.1:8080;
    server_name {HOSTNAME};
    location / {{
        proxy_pass http://127.0.0.1:4180;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }}
}}
{END}
"""


def proposed(nginx, tunnel):
    if BEGIN in nginx:
        if BLOCK.strip() not in nginx:
            raise ValueError("Existing managed Nginx block differs; review before replacing it.")
    elif HOSTNAME in nginx:
        raise ValueError("Hostname already has an unmanaged Nginx route.")
    else:
        nginx = nginx.rstrip() + "\n\n" + BLOCK
    route = f"  - hostname: {HOSTNAME}\n    service: http://127.0.0.1:8080"
    if f"hostname: {HOSTNAME}" in tunnel:
        if route not in tunnel:
            raise ValueError("Hostname already has a different Tunnel route.")
    else:
        fallback = "  - service: http_status:404"
        if tunnel.count(fallback) != 1:
            raise ValueError("Expected exactly one terminal Tunnel 404 rule.")
        tunnel = tunnel.replace(fallback, route + "\n\n" + fallback)
    return nginx, tunnel


def atomic(path, content, metadata):
    descriptor, temporary = tempfile.mkstemp(dir=path.parent, prefix=".adofai-ipc-")
    try:
        with os.fdopen(descriptor, "w") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, metadata.st_mode & 0o777)
        os.chown(temporary, metadata.st_uid, metadata.st_gid)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    for path in (NGINX, TUNNEL):
        if path.is_symlink():
            raise SystemExit(f"Refusing unexpected symlink: {path}")
    old_nginx, old_tunnel = NGINX.read_text(), TUNNEL.read_text()
    nginx, tunnel = proposed(old_nginx, old_tunnel)
    print(f"{HOSTNAME} -> Tunnel -> host Nginx :8080 -> docs :4180/docs/")
    print(f"Nginx change: {nginx != old_nginx}; Tunnel change: {tunnel != old_tunnel}")
    if not args.apply:
        print("Preview only. Apply with: sudo python3 bootstrap-routing.py --apply")
        return
    if os.geteuid() != 0:
        raise SystemExit("Run --apply with sudo; no password is stored by this script.")
    nginx_metadata, tunnel_metadata = NGINX.stat(), TUNNEL.stat()
    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    backup = Path("/var/backups/adofai-ipc-routing") / timestamp
    backup.mkdir(parents=True, mode=0o700)
    shutil.copy2(NGINX, backup / "nginx.conf")
    shutil.copy2(TUNNEL, backup / "tunnel.yml")
    cloudflared = shutil.which("cloudflared")
    if not cloudflared:
        raise SystemExit("cloudflared is missing.")
    try:
        atomic(NGINX, nginx, nginx_metadata)
        atomic(TUNNEL, tunnel, tunnel_metadata)
        subprocess.run(["nginx", "-t"], check=True)
        subprocess.run([cloudflared, "tunnel", "--config", str(TUNNEL), "ingress", "validate"], check=True)
        subprocess.run(["runuser", "-u", "kgh", "--", cloudflared, "tunnel", "route", "dns", "impl1113-home", HOSTNAME], check=True)
        subprocess.run(["loginctl", "enable-linger", "kgh"], check=True)
        subprocess.run(["systemctl", "reload", "nginx"], check=True)
        if tunnel != old_tunnel:
            subprocess.run(["systemctl", "restart", "cloudflared"], check=True)
    except Exception:
        atomic(NGINX, old_nginx, nginx_metadata)
        atomic(TUNNEL, old_tunnel, tunnel_metadata)
        subprocess.run(["nginx", "-t"], check=False)
        subprocess.run(["systemctl", "reload", "nginx"], check=False)
        subprocess.run(["systemctl", "restart", "cloudflared"], check=False)
        raise
    print(f"Routing installed. Previous configurations: {backup}")


if __name__ == "__main__":
    main()
