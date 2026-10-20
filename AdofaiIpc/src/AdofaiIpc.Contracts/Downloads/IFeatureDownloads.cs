using System;
using System.IO;
using System.Threading;

namespace AdofaiIpc.Contracts;

/// <summary>Optional runtime capability. Exported sources transfer ownership even on failure.</summary>
public interface IFeatureDownloads
{
  IpcFileTicket ExportFile(Stream source, long byteLength, string fileName, string contentType, CancellationToken sessionCancellation, CancellationToken callCancellation);
  IpcFileTicket ExportFile(Action<Stream> writer, long byteLength, string fileName, string contentType, CancellationToken sessionCancellation, CancellationToken callCancellation);
}

public sealed class IpcFileTransferException : Exception
{
  public string Code { get; }
  public IpcFileTransferException(string code, string message) : base(message) { Code = code; }
}

/// <summary>Low-level transfer lease used internally by the typed SDK.</summary>
public sealed class IpcFileTicket : IDisposable
{
  private Action _revoke;
  public string Url { get; }
  public long ByteLength { get; }
  public string FileName { get; }
  public string ContentType { get; }
  public IpcFileTicket(string url, long byteLength, string fileName, string contentType, Action revoke)
  { Url = url; ByteLength = byteLength; FileName = fileName; ContentType = contentType; _revoke = revoke; }
  public void Dispose() => Interlocked.Exchange(ref _revoke, null)?.Invoke();
}
