# Lifecycle and threading

[Guide](000-DevelopGuide.md) | [한국어](../kor/004-Lifecycle.md)

The process host owns one persistent dispatcher and one WebSocket server. Consumer mods own
only feature registrations and any local peers they create. Disabling the first consumer or
disposing every registration leaves the host alive. Unity quitting/unloading shuts it down.

Install commands and listeners during configure, prepare domain state, then MarkReady.
MarkInitializing begins another setup cycle; MarkError reports failure. Ready transitions
hydrate current subscribers. Reconnects receive snapshots without replaying mutating commands.

Command handlers select Background or GameThread. Async continuations are not guaranteed to
stay on the game thread: use runtime.RunOnGameThread after asynchronous work when touching Unity.
Feature lifecycle/event callbacks use the game thread; local peer Message uses a worker.
Dispose callbacks and cancellation callbacks must not block waiting for the game thread.
Keep work short. The dispatcher admits at most 1024 queued actions and drains 64 per frame.

Feature disposal cancels queued commands immediately; a queued callback cannot later execute
cancelled work. Running commands retain their admission slot until completion and must observe
CancellationToken. Disposal revokes unconsumed download tickets. Local peer lifetime is explicit:
dispose peers owned by a consumer when it disables, even though the shared host remains alive.

Domain revisions, ownership, leases, timeouts and recovery policies belong to the application.
Do not perform filesystem, network, archive or long-running work on the game thread.
