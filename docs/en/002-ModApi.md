# Mod API

[Guide](000-DevelopGuide.md) | [한국어](../kor/002-ModApi.md)

Register one namespace per mod. A fresh registration begins in initializing state. Replacing
a registration cancels the old handle, removes its handlers and tickets, and creates a fresh
handle; register all handlers again. Registering an existing command name replaces that handler.

```csharp
using AdofaiIpc;

var ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace("example-mod", new IpcNamespaceInfo {
    DisplayName = "Example Mod", Version = "1.0.0",
    AllowedOrigins = new[] { "https://example.com", "http://localhost", "http://127.0.0.1" }
});
ipc.RegisterMainThreadCommand("level.open", command => {
    string levelId = (string)command.Payload["levelId"];
    if (string.IsNullOrEmpty(levelId)) {
        command.Reject("invalid_level", "Choose a level first.");
        return;
    }
    OpenLevel(levelId); // application service; Unity work runs on the game thread
    command.Reply("level.opened", new { levelId });
});
ipc.PeerSubscribed += peer =>
    ipc.SendToPeer(peer.PeerId, "library.snapshot", new { revision = 1 });
ipc.MarkReady();

// On a domain change:
ipc.Publish("library.changed", new { revision = 2 });
// On disable/unload:
AdofaiIpc.AdofaiIpc.UnregisterNamespace("example-mod");
```

`RegisterCommand(name, Action<IpcCommand>)` runs on a background worker.
`RegisterAsyncCommand(name, Func<IpcCommand,Task>)` supports background asynchronous work.
`RegisterMainThreadCommand(name, Action<IpcCommand>)` queues work for the game thread.
Every handler must describe its domain outcome with `Reply`, `Reject`, and/or published events.
`accepted` means validated and admitted to execution, not completed. Exceptions produce
`command.failed`. A handler that returns silently has no automatic domain result.

`IpcCommand` exposes Id, Namespace, Name, PeerId, ConnectionId, Origin, JObject Payload and
CancellationToken. The peer identities are assigned by the server, never trusted from payload.
Cancellation follows connection loss and namespace unload. Use it for long operations, and
bind jobs, leases or privileged tokens to the peer in your application layer.

`Publish(name,payload)` sends an event to all authorized subscribers. `SendToPeer(peerId,name,
payload,correlationId)` sends an event to one subscribed peer. `SendCommandToPeer(peerId,name,
payload)` initiates a reverse command and returns its ID. Consume its named replies through
`PeerEvent`; correlate with CorrelationId. Subscribe to `PeerSubscribed` for hydration and
`PeerDisconnected` for owner cleanup. Snapshot callbacks fire when the namespace is ready.

Namespace names use lowercase letters, digits and hyphens. Command/event names also allow dots.
`AllowedOrigins` restricts subscription and messaging. A configured default-port origin permits
the same scheme and host on development ports; an explicit non-default port is matched exactly.
Native peers without Origin are allowed. An empty allowlist means unrestricted messaging.

Same-process mods can use `AdofaiIpc.AdofaiIpc.CreateLocalPeer(name)`: install the Message listener,
Subscribe(namespace), SendCommand or SendEvent, then Dispose. This uses the shared router and
the same command/state contract without another socket or HTTP polling. Wrap it in an application
port rather than making domain services reference the concrete local peer.
