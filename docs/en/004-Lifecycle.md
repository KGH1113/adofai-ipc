# Lifecycle and threading

[Guide](000-DevelopGuide.md) | [한국어](../kor/004-Lifecycle.md)

Register handlers and peer listeners while initializing. Complete data setup, then MarkReady.
MarkError(code,message) reports initialization failure. MarkInitializing starts a later setup
cycle. Commands are gated until ready. On disable, UnregisterNamespace cancels queued/running
handler tokens, revokes tickets, publishes unavailable and triggers peer-owner cleanup.

PeerSubscribed fires after the ready lifecycle snapshot; MarkReady also hydrates peers that
subscribed while the mod was missing or initializing. Do not send initial state before installing
the peer listeners. On the client, install snapshot/event/command listeners before connecting or
awaiting readiness. Revisioned snapshots and deltas belong to the domain, not the generic SDK.

Command workers, PeerSubscribed, PeerDisconnected and PeerEvent can run away from the Unity main
thread. Use an injected dispatcher for Unity access in lifecycle/event callbacks. Only
RegisterMainThreadCommand selects the game thread automatically. Keep these handlers short;
background downloads, archive work, waits and socket I/O must not block a frame.

On reconnect, the SDK reopens a connection with new identities, resubscribes, invalidates old
namespace readiness, and receives new snapshots. It never replays commands. Pending domain
operations must be rejected/cancelled or reconciled by the application session. Terminal wire
mismatch stops retrying; initial absence is UNAVAILABLE and the application supervisor may retry.

Keep operation ownership distinct from connection heartbeat. For example, a render session may
need its own lease, virtual clock, input gate and frame acknowledgment. Allow only one frame in
flight if file consumption must finish before the next frame is produced. Close/dispose must
restore any gate and release owned files/resources even when the UI process has already exited.
