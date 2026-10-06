# ADOFAI-IPC v1 developer guide

[한국어](../kor/000-DevelopGuide.md)

ADOFAI-IPC is a shared local WebSocket gateway for A Dance of Fire and Ice mods. Mods and
applications are peers: either side sends commands, replies with named events, or publishes
state changes. Product v1.0.0 uses wire protocol 3. The HTTP RPC API is removed.

One UnityModManager mod owns the listener. Consumer mods own their namespace, domain contract,
operation lifecycle, and state. The transport routes messages and manages connection lifetime.
It does not turn every command into a generic request/result call.

1. [Getting started](001-GettingStarted.md)
2. [Mod API](002-ModApi.md)
3. [WebSocket message protocol](003-MessageProtocol.md)
4. [Lifecycle and threading](004-Lifecycle.md)
5. [TypeScript SDK](005-JsClient.md)
6. [Dependency bootstrap](006-DependencyBootstrap.md)
7. [Streaming downloads](007-Downloads.md)

Keep application code dependent on its own message/session, scheduler, clock and storage ports.
Inject SDK, Unity and native implementations at the composition root. Domain events drive
projections; use an initial snapshot followed by revisioned changes instead of status polling.
