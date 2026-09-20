using System;
using System.IO;

namespace AdofaiIpc;

/// <summary>
/// Describes the bytes that an IPC download ticket will expose.
/// </summary>
public class IpcDownloadSource : IDisposable
{
  private readonly Stream _content;
  private readonly Action<Stream> _writeTo;
  private bool _disposed;

  public Stream Content => _content;
  public Action<Stream> WriteTo => _writeTo;
  public long ByteLength { get; }
  public long ContentLength => ByteLength;
  public string FileName { get; }
  public string ContentType { get; }

  public IpcDownloadSource(
    Stream content,
    long byteLength,
    string fileName,
    string contentType = "application/octet-stream")
  {
    if (content == null) throw new ArgumentNullException(nameof(content));
    if (!content.CanRead) throw new ArgumentException("The download stream must be readable.", nameof(content));

    ValidateLength(byteLength);
    _content = content;
    ByteLength = byteLength;
    FileName = NormalizeFileName(fileName);
    ContentType = NormalizeContentType(contentType);
  }

  protected IpcDownloadSource(
    Action<Stream> writeTo,
    long byteLength,
    string fileName,
    string contentType = "application/octet-stream")
  {
    if (writeTo == null) throw new ArgumentNullException(nameof(writeTo));

    ValidateLength(byteLength);
    _writeTo = writeTo;
    ByteLength = byteLength;
    FileName = NormalizeFileName(fileName);
    ContentType = NormalizeContentType(contentType);
  }

  internal void WriteContent(Stream destination)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(IpcDownloadSource));
    if (destination == null) throw new ArgumentNullException(nameof(destination));

    if (_content != null)
    {
      CopyExactly(_content, destination, ByteLength);
      return;
    }

    var limited = new LimitedWriteStream(destination, ByteLength);
    _writeTo(limited);
    if (limited.Written != ByteLength)
    {
      throw new InvalidDataException(
        "The download writer produced " + limited.Written + " bytes; expected " + ByteLength + ".");
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    _content?.Dispose();
  }

  private static void CopyExactly(Stream source, Stream destination, long byteLength)
  {
    byte[] buffer = new byte[64 * 1024];
    long remaining = byteLength;

    while (remaining > 0)
    {
      int requested = (int)Math.Min(buffer.Length, remaining);
      int read = source.Read(buffer, 0, requested);
      if (read <= 0)
      {
        throw new EndOfStreamException(
          "The download stream ended before the declared byte length was written.");
      }

      destination.Write(buffer, 0, read);
      remaining -= read;
    }
  }

  private static void ValidateLength(long byteLength)
  {
    if (byteLength < 0)
    {
      throw new ArgumentOutOfRangeException(nameof(byteLength), "The download byte length cannot be negative.");
    }
  }

  private static string NormalizeFileName(string fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName)) return "download";
    return fileName.Length > 255 ? fileName.Substring(0, 255) : fileName;
  }

  private static string NormalizeContentType(string contentType)
  {
    if (string.IsNullOrWhiteSpace(contentType)) return "application/octet-stream";
    if (contentType.Length > 256) return "application/octet-stream";

    for (int i = 0; i < contentType.Length; i++)
    {
      char character = contentType[i];
      if (character == '\r' || character == '\n' || character < 0x20 || character == 0x7f)
      {
        return "application/octet-stream";
      }
    }

    return contentType;
  }

  private sealed class LimitedWriteStream : Stream
  {
    private readonly Stream _inner;
    private readonly long _limit;

    public long Written { get; private set; }

    public LimitedWriteStream(Stream inner, long limit)
    {
      _inner = inner;
      _limit = limit;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _limit;
    public override long Position
    {
      get => Written;
      set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
      throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      if (count < 0 || Written > _limit - count)
      {
        throw new InvalidDataException("The download writer exceeded the declared byte length.");
      }

      _inner.Write(buffer, offset, count);
      Written += count;
    }

    public override void WriteByte(byte value)
    {
      if (Written >= _limit)
      {
        throw new InvalidDataException("The download writer exceeded the declared byte length.");
      }

      _inner.WriteByte(value);
      Written++;
    }

    protected override void Dispose(bool disposing)
    {
      // The callback does not own the HTTP response stream.
      base.Dispose(disposing);
    }
  }
}
