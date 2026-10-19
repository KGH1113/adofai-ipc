using System;
using System.IO;

namespace AdofaiIpc;

/// <summary>
/// Callback-based download source for handlers that want to stream bytes without
/// opening or buffering the complete payload before the ticket is consumed.
/// </summary>
public sealed class IpcDownloadResponse : IpcDownloadSource
{
  public IpcDownloadResponse(
    string contentType,
    long contentLength,
    string fileName,
    Action<Stream> writeTo)
    : base(writeTo, contentLength, fileName, contentType)
  {
  }

  public IpcDownloadResponse(
    Stream content,
    long contentLength,
    string fileName,
    string contentType = "application/octet-stream")
    : base(content, contentLength, fileName, contentType)
  {
  }
}
