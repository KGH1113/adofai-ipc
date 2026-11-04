using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using AdofaiIpc.Core;
using AdofaiIpc.Transport.ManagedWebSocket.Net;
using AdofaiIpc.Transport.ManagedWebSocket.Server;
using Newtonsoft.Json;

namespace AdofaiIpc.Server;

/// <summary>
/// Opened files over HTTP: <c>POST /ipc/files/open/{downloadToken}</c> opens a received file,
/// <c>GET /ipc/files/{token}</c> reads it with optional byte ranges, and
/// <c>POST /ipc/files/{token}/close</c> releases it.
/// </summary>
internal sealed class FileEndpoint
{
  private const string Prefix = "/ipc/files/";
  private const string OpenPrefix = Prefix + "open/";
  private const string CloseSuffix = "/close";
  private const int ChunkBytes = 64 * 1024;

  private readonly IOpenedFiles _files;
  // A playing video keeps a request or two open; separate slots keep downloads unaffected.
  private readonly SemaphoreSlim _readSlots = new SemaphoreSlim(16, 16);

  internal FileEndpoint(IOpenedFiles files) { _files = files; }

  internal static bool Handles(string path) => path.StartsWith(Prefix, StringComparison.Ordinal);

  internal void Get(HttpRequestEventArgs e)
  {
    string token = e.Request.Url.AbsolutePath.Substring(Prefix.Length);
    string origin = e.Request.Headers["Origin"];
    if (!_readSlots.Wait(0)) { e.Response.StatusCode = 503; e.Response.Headers["Retry-After"] = "1"; return; }
    try
    {
      if (!_files.TryRead(token, origin, out IOpenedFileRead read, out string approved, out string code, out string reason))
      { Error(e.Response, code, reason, approved); return; }
      using (read)
      {
        ByteRange range = ByteRangeRequest.Parse(e.Request.Headers["Range"], read.ByteLength);
        HttpListenerResponse response = e.Response;
        response.ContentType = read.ContentType;
        response.KeepAlive = false;
        response.Headers["Accept-Ranges"] = "bytes";
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        // A document opened from here must not run script on the runtime's origin.
        response.Headers["Content-Security-Policy"] = "sandbox";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        response.Headers["Content-Disposition"] = "inline; filename*=UTF-8''" + Uri.EscapeDataString(read.FileName);
        Grant(response, approved, "Accept-Ranges, Content-Length, Content-Range");

        if (range.Kind == ByteRangeKind.Unsatisfiable)
        {
          response.StatusCode = 416;
          response.Headers["Content-Range"] = "bytes */" + Number(read.ByteLength);
          response.ContentLength64 = 0;
          return;
        }

        if (range.Kind == ByteRangeKind.Partial)
        {
          response.StatusCode = 206;
          response.Headers["Content-Range"] = "bytes " + Number(range.Start) + "-" + Number(range.End - 1) + "/" + Number(read.ByteLength);
        }

        response.ContentLength64 = range.Length;
        response.StreamsFixedLengthBody = true;
        Copy(read, range, response.OutputStream);
      }
    }
    catch (Exception error)
    {
      // Browsers cancel media requests whenever they seek or have buffered enough.
      if (!(error is IOException || error is ObjectDisposedException)) System.Diagnostics.Trace.TraceError(error.ToString());
      e.Response.Abort();
    }
    finally { _readSlots.Release(); }
  }

  internal void Post(HttpRequestEventArgs e)
  {
    string path = e.Request.Url.AbsolutePath;
    string origin = e.Request.Headers["Origin"];
    e.Response.Headers["Cache-Control"] = "no-store";
    try
    {
      if (path.StartsWith(OpenPrefix, StringComparison.Ordinal))
      {
        string downloadToken = path.Substring(OpenPrefix.Length);
        if (_files.TryOpen(downloadToken, origin, out string url, out string approved, out string code, out string reason))
        { Grant(e.Response, approved, null); Json(e.Response, 200, new { url }); }
        else Error(e.Response, code, reason, approved);
        return;
      }

      if (path.EndsWith(CloseSuffix, StringComparison.Ordinal))
      {
        string token = path.Substring(Prefix.Length, path.Length - Prefix.Length - CloseSuffix.Length);
        if (_files.TryClose(token, origin, out string approved, out string code, out string reason))
        { Grant(e.Response, approved, null); e.Response.StatusCode = 204; }
        else Error(e.Response, code, reason, approved);
        return;
      }

      e.Response.StatusCode = 404;
    }
    catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); e.Response.Abort(); }
  }

  /// <summary>Lets a page read a range with fetch, which may send a preflight for the Range header.</summary>
  internal void Options(HttpRequestEventArgs e)
  {
    string token = e.Request.Url.AbsolutePath.Substring(Prefix.Length);
    string origin = e.Request.Headers["Origin"];
    if (!_files.TryAuthorize(token, origin, out string approved, out string code, out string reason) || approved == null)
    { e.Response.StatusCode = code == IpcErrorCodes.OriginNotAllowed ? 403 : 404; return; }
    Grant(e.Response, approved, null);
    e.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST";
    e.Response.Headers["Access-Control-Allow-Headers"] = "Range";
    e.Response.Headers["Access-Control-Max-Age"] = "600";
    if (string.Equals(e.Request.Headers["Access-Control-Request-Private-Network"], "true", StringComparison.OrdinalIgnoreCase))
      e.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    e.Response.StatusCode = 204;
  }

  private static void Copy(IOpenedFileRead read, ByteRange range, Stream output)
  {
    byte[] buffer = new byte[ChunkBytes];
    long position = range.Start;
    while (position < range.End)
    {
      int count = read.ReadAt(position, buffer, 0, (int)Math.Min(buffer.Length, range.End - position));
      if (count <= 0) throw new EndOfStreamException("The file ended before its declared length.");
      output.Write(buffer, 0, count);
      output.Flush();
      position += count;
    }
  }

  private static void Grant(HttpListenerResponse response, string approved, string exposedHeaders)
  {
    // This value comes from the file's copied allowlist, never raw headers.
    if (approved == null) return;
    response.Headers["Access-Control-Allow-Origin"] = approved;
    response.Headers["Vary"] = "Origin";
    if (exposedHeaders != null) response.Headers["Access-Control-Expose-Headers"] = exposedHeaders;
  }

  private static void Error(HttpListenerResponse response, string code, string message, string approved)
  {
    Grant(response, approved, null);
    int status =
      code == IpcErrorCodes.OriginNotAllowed ? 403
      : code == IpcErrorCodes.FileNotSeekable ? 409
      : code == IpcErrorCodes.FileOpenLimit ? 429
      : code == IpcErrorCodes.DownloadNotFound || code == IpcErrorCodes.FileExpired ? 404
      : 503;
    string publicCode = code == IpcErrorCodes.DownloadNotFound ? IpcErrorCodes.FileExpired : code;
    Json(response, status, new { code = publicCode, message });
  }

  private static void Json(HttpListenerResponse response, int status, object body)
  {
    byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body));
    response.StatusCode = status;
    response.ContentType = "application/json";
    response.ContentLength64 = data.Length;
    response.OutputStream.Write(data, 0, data.Length);
  }

  private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
