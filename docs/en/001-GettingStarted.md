# AdofaiIpc Developer Guide - Getting Started

[Guide index](000-DevelopGuide.md) | [Korean](../kor/001-GettingStarted.md)

1. [What is AdofaiIpc?](#1-what-is-adofaiipc)
2. [Runtime requirements](#2-runtime-requirements)
3. [Port policy](#3-port-policy)
4. [Health check](#4-health-check)

---

## 1. What is AdofaiIpc?

AdofaiIpc is a local IPC gateway between ADOFAI mods and external clients.

Browser userscripts, web UIs, CLIs, and development tools send HTTP requests to AdofaiIpc.
AdofaiIpc routes each request to a mod handler using the request's `namespace` and `method`.

Mod developers only register methods through the public API. Individual mods do not need to
create their own `HttpListener`.

---

## 2. Runtime requirements

AdofaiIpc runs directly through UnityModManager without another framework mod.

Required components:

- A Dance of Fire and Ice
- UnityModManager
- AdofaiIpc installed at `Mods/AdofaiIpc`

Mods that use the public API must reference `AdofaiIpc.dll` when they are built.
Mods that include `AdofaiIpc.Bootstrap.dll` can also install the latest package from GitHub
Releases automatically when AdofaiIpc is missing.

---

## 3. Port policy

AdofaiIpc first attempts to listen at:

```text
http://127.0.0.1:32145/
```

If the port is unavailable, it tries each port from `32146` through `32155` until it finds an
available port.

Clients should probe `/ipc/health` from `32145` upward instead of assuming that the default port
is always available.

---

## 4. Health check

With ADOFAI running and AdofaiIpc enabled, call the health endpoint:

```bash
curl -s http://127.0.0.1:32145/ipc/health
```

If AdofaiIpc selected a fallback port, replace `32145` with that port. A valid response contains
the server name, protocol version, and active port.
