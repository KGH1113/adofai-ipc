# AdofaiIpc Developer Guide - Lifecycle

[Guide index](000-DevelopGuide.md) | [Korean](../004-Lifecycle.md)

1. [Listener ownership](#1-listener-ownership)
2. [Register and unregister](#2-register-and-unregister)
3. [Thread selection](#3-thread-selection)
4. [Origin policy lifecycle](#4-origin-policy-lifecycle)
5. [Mod lifecycle pattern](#5-mod-lifecycle-pattern)

---

## 1. Listener ownership

AdofaiIpc is the only mod that owns the localhost HTTP listener. Consumer mods register
namespaces and methods in the shared registry instead of opening additional servers.

This prevents port conflicts and gives browser clients one protocol for multiple mods.

---

## 2. Register and unregister

Registering an existing namespace or method updates the previous registration, so registration
code can safely run again after a mod is re-enabled.

Unregister the namespace when the owning mod is disabled:

```csharp
public void Disable() {
    AdofaiIpc.AdofaiIpc.UnregisterNamespace("example-mod");
}
```

This keeps discovery output and handler ownership synchronized with the actual mod state.

---

## 3. Thread selection

Regular handlers run on the AdofaiIpc HTTP listener thread.

```csharp
ipc.Register("metadata.get", GetMetadata);
```

Use regular handlers for validation, serialization, immutable data, and other work that does not
touch Unity objects.

Handlers that access Unity scenes, GameObjects, UI, or ADOFAI state must use
`RegisterMainThread`:

```csharp
ipc.RegisterMainThread("level.open", OpenLevel);
```

A synchronous main-thread handler has a timeout. Keep main-thread work short and move network,
archive, and other expensive operations to background work.

---

## 4. Origin policy lifecycle

Origin restrictions belong to namespace metadata and are replaced when the namespace is
registered again.

```csharp
AdofaiIpc.AdofaiIpc.RegisterNamespace(
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

Unregistering the namespace removes its handlers and origin policy together.

---

## 5. Mod lifecycle pattern

```csharp
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
        ipc.RegisterMainThread("level.open", OpenLevel);
        enabled = true;
    }

    public void Disable() {
        if (!enabled) return;

        AdofaiIpc.AdofaiIpc.UnregisterNamespace(Namespace);
        enabled = false;
    }

    private static object OpenLevel(AdofaiIpc.Core.IpcRequest request) {
        return new { accepted = true };
    }
}
```
