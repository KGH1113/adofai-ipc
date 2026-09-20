# Streaming downloads

Since AdofaiIpc 0.4.0, a mod can register a download method on the existing localhost
listener. It needs neither a separate server nor a browser `Blob`.

```csharp
ipc.RegisterDownload("recording.export", request =>
{
  // Resolve a caller-provided ID in your own storage; do not turn it into a path.
  long byteLength = FindRecordingLength(request);
  return new IpcDownloadResponse(
    "audio/wav",
    byteLength,
    "recording.wav",
    destination => WriteRecordingTo(request, destination));
});
```

The handler returns `IpcDownloadSource` or `IpcDownloadError`.
`IpcDownloadResponse` is a convenience form of `IpcDownloadSource`. The callback runs on
an HTTP worker when the ticket is consumed, so it must not call Unity APIs directly. It
must write exactly the declared byte length and must not close the supplied stream. With
the stream constructor, AdofaiIpc closes the source when the download or ticket ends.

Clients invoke the method with the existing `POST /ipc` call. The successful response
contains a small `result` object with `Url` and `ByteLength`. Opening `Url` as a link
lets the browser save the `Content-Disposition: attachment` response using its native
download flow, without reading the complete audio into JavaScript memory.

Tickets expire after 60 seconds and are single-use. At most 128 tickets and four active
downloads are allowed. Unregistering a namespace or stopping the server revokes pending
tickets. The namespace Origin policy applies when creating a ticket and when a download
request has an Origin header. Native browser downloads may omit that header, so treat a
ticket URL as a secret and do not expose it in logs or shared pages.
