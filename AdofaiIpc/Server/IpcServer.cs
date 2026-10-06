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

public sealed class IpcServer
{
  private readonly IpcRegistry _registry;
  private readonly SemaphoreSlim _downloadSlots = new SemaphoreSlim(4, 4);
  private HttpServer _server;
  private System.Threading.Timer _heartbeat;
  public int Port { get; private set; }
  public string Url => Port == 0 ? null : "http://127.0.0.1:" + Port + "/";
  public IpcServer(IpcRegistry registry) { _registry = registry; }
  public void Start()
  {
    if (_server != null) return;
    Exception last = null;
    for (int port = 32145; port <= 32155; port++)
    {
      var server = new HttpServer(IPAddress.Loopback, port) { KeepClean = true, WaitTime = TimeSpan.FromSeconds(2) };
      server.Log.Level = LogLevel.Error;
      server.AddWebSocketService<IpcSocket>(IpcProtocol.WebSocketPath, socket => socket.Initialize(_registry));
      server.OnGet += (_, e) => Download(e);
      try { server.Start(); if (!server.IsListening) throw new IOException("IPC port is unavailable."); }
      catch (Exception error) { last = error; server.Stop(); continue; }
      _server = server; Port = port; _registry.DownloadBaseUrl = Url;
      _heartbeat = new System.Threading.Timer(_ => _registry.Heartbeat(), null, 10_000, 10_000);
      return;
    }
    throw new InvalidOperationException("Could not start the localhost IPC server.", last);
  }
  public void Stop()
  {
    _heartbeat?.Dispose(); _heartbeat = null;
    _server?.Stop(); _server = null; Port = 0;
    _registry.DownloadBaseUrl = null; _registry.ClearDownloadTickets();
  }
  private void Download(HttpRequestEventArgs e)
  {
    string path = e.Request.Url.AbsolutePath;
    if (path == "/ipc/health")
    {
      byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { server = "AdofaiIpc", serverVersion = IpcProtocol.ProductVersion, protocolVersion = IpcProtocol.Version, port = Port }));
      e.Response.ContentType = "application/json"; e.Response.ContentLength64 = data.Length; e.Response.OutputStream.Write(data, 0, data.Length); return;
    }
    if (!path.StartsWith("/ipc/download/", StringComparison.Ordinal)) { e.Response.StatusCode = 404; return; }
    if (!_downloadSlots.Wait(0)) { e.Response.StatusCode = 503; return; }
    try
    {
      string token = path.Substring("/ipc/download/".Length);
      if (!_registry.TryTakeDownloadTicket(token, e.Request.Headers["Origin"], out var source, out var approvedBrowserOrigin, out var code, out _))
      { e.Response.StatusCode = code == IpcErrorCodes.OriginNotAllowed ? 403 : 404; return; }
      using (source)
      {
        e.Response.ContentType = source.ContentType;
        e.Response.SendChunked = true; e.Response.KeepAlive = false;
        // This value comes from the ticket's copied allowlist, never raw headers.
        if (approvedBrowserOrigin != null)
        {
          e.Response.Headers["Access-Control-Allow-Origin"] = approvedBrowserOrigin;
          e.Response.Headers["Vary"] = "Origin";
          e.Response.Headers["Access-Control-Expose-Headers"] = "X-Byte-Length, Content-Disposition";
        }
        e.Response.Headers["Cache-Control"] = "no-store";
        e.Response.Headers["X-Content-Type-Options"] = "nosniff";
        e.Response.Headers["X-Byte-Length"] = source.ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(source.FileName);
        using (var destination = new FlushingStream(e.Response.OutputStream)) source.WriteContent(destination);
      }
    }
    catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); e.Response.Abort(); }
    finally { _downloadSlots.Release(); }
  }

  public sealed class IpcSocket : WebSocketBehavior, IIpcPeer
  {
    private IpcRegistry _registry;
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
    private int _sending, _queuedBytes, _count;
    public IpcPeerInfo Info { get; private set; }
    CancellationToken IIpcPeer.CancellationToken => _lifetime.Token;
    internal void Initialize(IpcRegistry registry) { _registry = registry; Protocol = IpcProtocol.Subprotocol; IgnoreExtensions = true; NoDelay = true; }
    protected override void OnOpen()
    {
      if (Protocol != IpcProtocol.Subprotocol) { CloseAsync(1002, "IPC protocol required."); return; }
      Info = new IpcPeerInfo { PeerId = Guid.NewGuid().ToString("N"), ConnectionId = Guid.NewGuid().ToString("N"), Origin = Headers["Origin"] };
      if (!_registry.Attach(this)) CloseAsync(1013, "Too many connections.");
    }
    protected override void OnMessage(MessageEventArgs e)
    {
      if (!e.IsText || e.RawData.Length > IpcProtocol.MaxMessageBytes) { CloseAsync(1009, "Control message exceeds the limit."); return; }
      try { _registry.Receive(this, JsonConvert.DeserializeObject<IpcPeerMessage>(e.Data, new JsonSerializerSettings { MaxDepth = 32, TypeNameHandling = TypeNameHandling.None })); }
      catch { CloseAsync(1007, "Invalid IPC message."); }
    }
    protected override void OnClose(CloseEventArgs e) { _lifetime.Cancel(); if (Info != null) _registry.Detach(this); while (_outgoing.TryDequeue(out _)) { } }
    void IIpcPeer.Close() => CloseAsync(1001, "IPC connection ended.");
    void IIpcPeer.Send(IpcPeerMessage message)
    {
      if (_lifetime.IsCancellationRequested) return;
      string text = JsonConvert.SerializeObject(message);
      int bytes = Encoding.UTF8.GetByteCount(text);
      int total = Interlocked.Add(ref _queuedBytes, bytes);
      int count = Interlocked.Increment(ref _count);
      if (bytes > IpcProtocol.MaxMessageBytes || total > 8 * 1024 * 1024 || count > 256)
      { Interlocked.Add(ref _queuedBytes, -bytes); Interlocked.Decrement(ref _count); CloseAsync(1013, "IPC consumer is too slow; reconnect to synchronize."); return; }
      _outgoing.Enqueue(text); Drain();
    }
    private void Drain()
    {
      if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return;
      _ = Task.Run(() => {
        try { while (!_lifetime.IsCancellationRequested && _outgoing.TryDequeue(out string text)) { Send(text); Interlocked.Add(ref _queuedBytes, -Encoding.UTF8.GetByteCount(text)); Interlocked.Decrement(ref _count); } }
        catch { CloseAsync(1001, "IPC send failed."); }
        finally { Interlocked.Exchange(ref _sending, 0); if (!_outgoing.IsEmpty && !_lifetime.IsCancellationRequested) Drain(); }
      });
    }
  }

  private sealed class FlushingStream : Stream
  {
    private readonly Stream _inner;
    internal FlushingStream(Stream inner) { _inner = inner; }
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
}
