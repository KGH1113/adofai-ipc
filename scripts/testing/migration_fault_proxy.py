#!/usr/bin/env python3
"""Local CONNECT proxy for manual updater failure tests; no TLS interception.

Configure only a disposable test game process to use this proxy. api.github.com
continues working while release asset hosts return 502. No OS proxy settings,
game files, release assets, certificates, or credentials are modified here.
"""

import argparse
import select
import socket
import socketserver


class FaultProxy(socketserver.StreamRequestHandler):
    def handle(self):
        request = self.rfile.readline(8193)
        if len(request) > 8192:
            return
        try:
            method, authority, _ = request.decode("ascii").strip().split(" ", 2)
            host, raw_port = authority.rsplit(":", 1)
            port = int(raw_port)
        except (ValueError, UnicodeError):
            return
        header_bytes = 0
        while True:
            line = self.rfile.readline(8193)
            header_bytes += len(line)
            if header_bytes > 32768 or not line or line in (b"\r\n", b"\n"):
                break
        if method != "CONNECT" or port != 443 or header_bytes > 32768:
            self.wfile.write(b"HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\n\r\n")
            return
        host = host.strip("[]").lower()
        if host in self.server.denied:
            print("BLOCK release asset host " + host, flush=True)
            self.wfile.write(b"HTTP/1.1 502 Fixture Download Failure\r\nContent-Length: 0\r\n\r\n")
            return
        try:
            upstream = socket.create_connection((host, port), timeout=15)
        except OSError:
            self.wfile.write(b"HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n")
            return
        print("ALLOW tunnel " + host, flush=True)
        with upstream:
            self.wfile.write(b"HTTP/1.1 200 Connection Established\r\n\r\n")
            self.wfile.flush()
            sockets = (self.connection, upstream)
            while True:
                try:
                    ready, _, errors = select.select(sockets, [], sockets, 30)
                    if not ready or errors:
                        return
                    for source in ready:
                        target = upstream if source is self.connection else self.connection
                        data = source.recv(65536)
                        if not data:
                            return
                        target.sendall(data)
                except OSError:
                    return


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8899)
    parser.add_argument("--deny-host", action="append", default=None)
    args = parser.parse_args()
    denied = args.deny_host or ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"]
    with Server(("127.0.0.1", args.port), FaultProxy) as server:
        server.denied = {host.lower() for host in denied}
        print(f"Fixture proxy listening on 127.0.0.1:{args.port}; blocked={','.join(sorted(server.denied))}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
