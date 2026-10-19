using System;

namespace AdofaiIpc;

/// <summary>A mod must transfer large data through a download ticket instead of a control message.</summary>
public sealed class IpcMessageTooLargeException : InvalidOperationException
{
  public int ByteLength { get; }
  public int MaxByteLength => IpcProtocol.MaxMessageBytes;

  internal IpcMessageTooLargeException(int byteLength)
    : base("IPC control message is " + byteLength + " bytes; the limit is " + IpcProtocol.MaxMessageBytes + ". Use a download ticket for large data.")
  { ByteLength = byteLength; }
}
