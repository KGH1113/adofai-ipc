using System;
using System.IO;
using System.Threading;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

/// <summary>A single-use file source. Ownership passes to the SDK when returned or sent to an app.</summary>
public sealed class IpcFile : IDisposable
{
  private readonly Stream _stream;
  private readonly Action<Stream> _writer;
  private int _state;
  public long Size { get; }
  public string Name { get; }
  public string ContentType { get; }
  private IpcFile(Stream stream, Action<Stream> writer, long size, string name, string contentType)
  {
    if (size < 0 || size > 9007199254740991L) throw new ArgumentOutOfRangeException(nameof(size));
    if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || HasControl(name)) throw new ArgumentException("A file name of 1–255 characters without control characters is required.", nameof(name));
    if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 256 || HasControl(contentType)) throw new ArgumentException("A valid content type is required.", nameof(contentType));
    _stream = stream; _writer = writer; Size = size; Name = name; ContentType = contentType;
  }
  public static IpcFile FromPath(string path, string contentType = "application/octet-stream")
  {
    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    try { return FromStream(stream, stream.Length, Path.GetFileName(path), contentType); }
    catch { stream.Dispose(); throw; }
  }
  public static IpcFile FromStream(Stream stream, long byteLength, string fileName, string contentType = "application/octet-stream")
  {
    if (stream == null) throw new ArgumentNullException(nameof(stream));
    if (!stream.CanRead) throw new ArgumentException("The file stream must be readable.", nameof(stream));
    return new IpcFile(stream, null, byteLength, fileName, contentType);
  }
  public static IpcFile FromWriter(Action<Stream> writer, long byteLength, string fileName, string contentType = "application/octet-stream")
    => new IpcFile(null, writer ?? throw new ArgumentNullException(nameof(writer)), byteLength, fileName, contentType);
  internal IpcFileTicket Export(IFeatureDownloads downloads, CancellationToken sessionCancellation, CancellationToken callCancellation)
  {
    if (downloads == null) { Dispose(); throw new IpcCallException("file_transfer_unavailable", "Update the IPC runtime and restart the game to transfer files."); }
    if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) throw new IpcValidationException("A file source can only be sent once.");
    try
    {
      return _stream != null ? downloads.ExportFile(_stream, Size, Name, ContentType, sessionCancellation, callCancellation)
        : downloads.ExportFile(_writer, Size, Name, ContentType, sessionCancellation, callCancellation);
    }
    catch (IpcFileTransferException error) { throw new IpcCallException(error.Code, error.Message); }
  }
  public void Dispose() { if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _stream?.Dispose(); }
  private static bool HasControl(string value) { foreach (char character in value) if (char.IsControl(character)) return true; return false; }
}
