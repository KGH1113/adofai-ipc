using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Core;
using AdofaiIpc.Transport.ManagedWebSocket;
using AdofaiIpc.Transport.ManagedWebSocket.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Server;

  internal sealed class FlushingDownloadStream : Stream
  {
    private readonly Stream _inner;
    internal FlushingDownloadStream(Stream inner) { _inner = inner; }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { while (count > 0) { int chunk = Math.Min(count, 64 * 1024); _inner.Write(buffer, offset, chunk); _inner.Flush(); offset += chunk; count -= chunk; } }
    public override void WriteByte(byte value) { var data = new[] { value }; Write(data, 0, 1); }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _inner.Flush(); base.Dispose(disposing); }
  }
