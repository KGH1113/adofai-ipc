# AdofaiIpc Developer Guide - Mod API

[Guide index](000-DevelopGuide.md) | [Korean](../kor/002-ModApi.md)

1. [Registering a namespace](#1-registering-a-namespace)
2. [Registering methods](#2-registering-methods)
3. [Unregistering](#3-unregistering)
4. [Origin restrictions](#4-origin-restrictions)
5. [Naming rules](#5-naming-rules)
6. [Lifecycle example](#6-lifecycle-example)
7. [Mode readiness](#7-mode-readiness)

---

## 1. Registering a namespace

Create one namespace for each mod. Namespace names prevent method collisions between mods.

```csharp
AdofaiIpcNamespace ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
    "example-mod",
    new IpcNamespaceInfo {
        DisplayName = "Example Mod",
        Version = "1.0.0"
    }
);
```

Registering the same namespace again updates its metadata, resets its state to `Initializing`, and
returns a namespace handle. Existing methods remain registered.

---

## 2. Registering methods

Use `Register` for handlers that do not access Unity state.

```csharp
ipc.Register("health", request => new {
    ok = true
});
```

Use `RegisterMainThread` when a handler reads or changes Unity or ADOFAI objects.

```csharp
ipc.RegisterMainThread("level.open", request => {
    // Access Unity or ADOFAI state here.
    return new {
        opened = true
    };
});
```

Registering the same method again replaces the previous handler. After all handlers and mode data
are ready, allow requests explicitly:

```csharp
ipc.MarkReady();
```

---

## 3. Unregistering

Remove one method from a namespace handle:

```csharp
ipc.Unregister("health");
```

Remove an entire namespace through the public facade:

```csharp
AdofaiIpc.AdofaiIpc.UnregisterNamespace("example-mod");
```

Unregister the namespace when the owning mod is disabled or unloaded.

---

## 4. Origin restrictions

Set `AllowedOrigins` to restrict browser requests for a namespace.

```csharp
AdofaiIpcNamespace ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
    "example-mod",
    new IpcNamespaceInfo {
        DisplayName = "Example Mod",
        Version = "1.0.0",
        AllowedOrigins = new[] {
            "https://example.com",
            "http://localhost",
            "http://127.0.0.1"
        }
    }
);
```

An origin with the same scheme and host as an allowed default-port origin is accepted even if
the request uses a different port. For example, allowing `http://localhost` also permits a local
development server such as `http://localhost:5173`.

Requests without an `Origin` header, such as curl and native local tools, are allowed. A null or
empty `AllowedOrigins` list does not restrict origins.

---

## 5. Naming rules

Namespace names may contain lowercase letters, numbers, and `-`.

```text
example-mod
tuf-replay
```

Method names may contain lowercase letters, numbers, `-`, and `.`.

```text
health
level.open
records.list
```

Namespace names cannot contain `.`.

---

## 6. Lifecycle example

Keep IPC registration in a small lifecycle component and make enable/disable operations
idempotent.

```csharp
using AdofaiIpc;

public sealed class IpcFeature {
    private const string Namespace = "example-mod";
    private bool enabled;

    public void Enable() {
        if (enabled) return;

        AdofaiIpcNamespace ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
            Namespace,
            new IpcNamespaceInfo {
                DisplayName = "Example Mod",
                Version = "1.0.0"
            }
        );

        ipc.Register("health", _ => new { ok = true });
        ipc.MarkReady();
        enabled = true;
    }

    public void Disable() {
        if (!enabled) return;

        AdofaiIpc.AdofaiIpc.UnregisterNamespace(Namespace);
        enabled = false;
    }
}
```

---

## 7. Mode readiness

Listener discovery and namespace registration do not prove that a mode has finished initializing.
AdofaiIpc therefore creates every registered namespace in the `Initializing` state and prevents
its methods from being called until the owner marks it ready.

```csharp
try {
    InitializeMode();
    ipc.MarkReady();
} catch (Exception error) {
    ipc.MarkError("initialization_failed", error.Message);
}
```

The registry exposes `initializing`, `ready`, and `error` through namespace discovery. Calls made
while initializing or after an initialization error fail with `namespace_initializing` or
`namespace_error` before a handler runs. Call `MarkInitializing()` before beginning a later
reinitialization cycle.
