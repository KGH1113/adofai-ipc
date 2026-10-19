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

internal sealed class DownloadEndpoint {
  private readonly IDownloadTickets _tickets;
  private readonly Func<int> _port;
  private readonly SemaphoreSlim _downloadSlots = new SemaphoreSlim(4, 4);
  internal DownloadEndpoint(IDownloadTickets tickets, Func<int> port) { _tickets = tickets; _port = port; }
  internal void Handle(HttpRequestEventArgs e)
  {
    string path = e.Request.Url.AbsolutePath;
    if (path == "/ipc/health")
    {
      byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { server = "AdofaiIpc", serverVersion = IpcProtocol.ProductVersion, protocolVersion = IpcProtocol.Version, port = _port() }));
      e.Response.ContentType = "application/json"; e.Response.ContentLength64 = data.Length; e.Response.OutputStream.Write(data, 0, data.Length); return;
    }
    if (!path.StartsWith("/ipc/download/", StringComparison.Ordinal)) { e.Response.StatusCode = 404; return; }
    if (!_downloadSlots.Wait(0)) { e.Response.StatusCode = 503; return; }
    try
    {
      string token = path.Substring("/ipc/download/".Length);
      if (!_tickets.TryTake(token, e.Request.Headers["Origin"], out var source, out var approvedBrowserOrigin, out var code, out _))
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
        using (var destination = new FlushingDownloadStream(e.Response.OutputStream)) source.WriteContent(destination);
      }
    }
    catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); e.Response.Abort(); }
    finally { _downloadSlots.Release(); }
  }

}
