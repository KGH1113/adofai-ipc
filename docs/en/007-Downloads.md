# Streaming downloads

[Guide](000-DevelopGuide.md) | [한국어](../kor/007-Downloads.md)

A named command result carries a one-use HTTP download ticket instead of embedding file bytes.

```csharp
feature.OnCommand("recording.export", IpcExecutionContext.Background, command => {
    var file = recordings.Find((string)JObject.Parse(command.Json)["recordingId"]);
    if (file == null) {
        command.Reject("recording_missing", "Record again.");
        return Task.CompletedTask;
    }
    command.ReplyDownload("recording.ready", file.OpenRead(), file.ByteLength,
        "recording.wav", "audio/wav", "{\"revision\":1}");
    return Task.CompletedTask;
});
```

ReplyDownload accepts either a readable Stream or Action<Stream>. The runtime takes ownership
of the source, including failure/cancellation paths. A writer runs on an HTTP worker and must
write exactly byteLength without closing the supplied response stream or reading Unity objects.

The result has {url, byteLength, metadata} and the command correlationId. Keep metadata small.
Download responses flush bounded 64 KiB chunks with Content-Disposition, X-Byte-Length,
no-store and nosniff. Open the URL as an attachment or fetch it from an authorized browser.

Tickets expire in 60 seconds, are one-use, and are limited to 128 tickets and 4 concurrent downloads.
Feature disposal and host shutdown revoke pending tickets. Authorization uses a copied feature
Origin allowlist; only an explicitly matched browser Origin gets CORS read access.
A rejected Origin does not consume the ticket. Native/attachment requests may omit Origin.
Treat URLs as temporary bearer capabilities belonging to the issuing application's flow.

Oversized control results produce correlated command.failed/message_too_large without closing
the connection. File bytes must remain in the stream.
