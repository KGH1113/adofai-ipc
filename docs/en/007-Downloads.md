# Streaming downloads

[Guide](000-DevelopGuide.md) | [한국어](../kor/007-Downloads.md)

Create a ticket with a WebSocket command and transfer file bytes through the HTTP stream:

```csharp
ipc.RegisterDownloadCommand("recording.export", command => {
    // Resolve a domain ID through storage; do not accept an arbitrary filesystem path.
    var file = recordings.Find((string)command.Payload["recordingId"]);
    if (file == null) return new IpcDownloadError("recording_missing", "Record again.");
    return new IpcDownloadSource(file.OpenRead(), file.ByteLength, "recording.wav", "audio/wav");
});
```

The handler returns IpcDownloadSource or IpcDownloadError. A public callback constructor accepts
Action<Stream>, byteLength, fileName, contentType; IpcDownloadResponse remains a convenience.
The writer runs on an HTTP worker when consumed, writes exactly the declared length, and leaves
the supplied response stream open. It must not read Unity objects there. Source streams are owned
and disposed by the ticket/download lifecycle.

The issuer receives the correlated event `download.ready` with lowercase `{url,byteLength}`.
Open url as an attachment link for native browser saving without a large JavaScript Blob. Fetch
is also available to allowed web applications. Responses use bounded64KiB flushed chunks,
Content-Disposition, X-Byte-Length, no-store and nosniff. Chunked transfer means Content-Length
is absent; use the byteLength from the event for expected size/progress.

Tickets expire after60s, are consumed once, and are bounded to128 tickets and4 concurrent downloads.
Namespace unload/server stop revokes pending sources. Ticket authorization uses a copied namespace
Origin policy. Browser CORS read access is granted only after successful ticket consumption with
an explicit matching allowlist; unrestricted/native tickets receive no CORS grant. Rejected Origin
does not consume the ticket. Attachment navigation/native tools may omit Origin. Treat URLs as
short-lived bearer capabilities and keep them scoped to the issuing peer's application flow.
