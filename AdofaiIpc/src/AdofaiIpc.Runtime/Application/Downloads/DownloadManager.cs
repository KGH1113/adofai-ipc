using System;
using System.Threading;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Core;
internal sealed class DownloadManager : IDownloadTickets, IOpenedFiles {
  private readonly IpcDownloadTicketStore _tickets = new IpcDownloadTicketStore();
  private readonly IpcOpenedFileStore _opened = new IpcOpenedFileStore();
  internal string BaseUrl { get; set; }
  internal void RevokeNamespace(string name) { _tickets.RevokeNamespace(name); _opened.RevokeNamespace(name); }
  internal void Clear() { _tickets.Clear(); _opened.Clear(); }
  internal void PruneIdleFiles(DateTime nowUtc) => _opened.PruneIdle(nowUtc);
  public bool TryOpen(string downloadToken, string origin, out string url, out string approvedBrowserOrigin, out string code, out string reason)
  {
    url = null;
    approvedBrowserOrigin = null;
    // Check first, so a full store leaves the ticket readable as a stream.
    if (!_opened.HasCapacity)
    {
      code = IpcErrorCodes.FileOpenLimit; reason = "Too many files are open. Close one and try again.";
      return false;
    }
    if (string.IsNullOrEmpty(BaseUrl))
    {
      code = "file_transfer_unavailable"; reason = "The file service is unavailable.";
      return false;
    }
    if (!_tickets.TryTakeSeekable(downloadToken, origin, out var ticket, out approvedBrowserOrigin, out code, out reason)) return false;
    if (!_opened.TryOpen(ticket, out string token, out code, out reason)) { ticket.Source.Dispose(); return false; }
    url = BaseUrl.TrimEnd('/') + "/ipc/files/" + token;
    return true;
  }
  public bool TryRead(string token, string origin, out IOpenedFileRead read, out string approvedBrowserOrigin, out string code, out string reason)
    => _opened.TryRead(token, origin, out read, out approvedBrowserOrigin, out code, out reason);
  public bool TryAuthorize(string token, string origin, out string approvedBrowserOrigin, out string code, out string reason)
    => _opened.TryAuthorize(token, origin, out approvedBrowserOrigin, out code, out reason);
  public bool TryClose(string token, string origin, out string approvedBrowserOrigin, out string code, out string reason)
    => _opened.TryClose(token, origin, out approvedBrowserOrigin, out code, out reason);
  internal IpcFileTicket Export(AdofaiIpcNamespace handle, IpcDownloadSource source,
    CancellationToken sessionCancellation, CancellationToken callCancellation)
  {
    string token = null;
    try
    {
      sessionCancellation.ThrowIfCancellationRequested(); callCancellation.ThrowIfCancellationRequested();
      if (string.IsNullOrEmpty(BaseUrl)) throw new IpcFileTransferException("file_transfer_unavailable", "The file service is unavailable.");
      if (!_tickets.TryCreate(handle.Name, source, handle.Info.AllowedOrigins, out token, out var code, out var reason, handle.Info.ExactOrigins))
        throw new IpcFileTransferException(code, reason);
      string key = token;
      // The file's session or mod ending also closes it after an app opened it.
      source.AttachLifetime(CancellationTokenSource.CreateLinkedTokenSource(handle.Lifetime.Token, sessionCancellation, callCancellation),
        () => { _tickets.Revoke(key); _opened.RevokeSource(source); });
      sessionCancellation.ThrowIfCancellationRequested(); callCancellation.ThrowIfCancellationRequested();
      return new IpcFileTicket(BaseUrl.TrimEnd('/') + "/ipc/download/" + token, source.ByteLength, source.FileName, source.ContentType,
        () => { _tickets.Revoke(key); source.Dispose(); });
    }
    catch { if (token != null) _tickets.Revoke(token); source.Dispose(); throw; }
  }
  internal bool TryTake(string token, string origin, out IpcDownloadSource source, out string code, out string reason)
    => _tickets.TryTake(token, origin, out source, out code, out reason);
  public bool TryTake(string token, string origin, out IpcDownloadSource source, out string approvedOrigin, out string code, out string reason)
    => _tickets.TryTake(token, origin, out source, out approvedOrigin, out code, out reason);
  internal void Reply(AdofaiIpcNamespace handle, IpcCommand command, object result, string eventName = "download.ready", object metadata = null)
  {
    if (command.IsRejected || command.CancellationToken.IsCancellationRequested) { (result as IDisposable)?.Dispose(); return; }
    if (result is IpcDownloadError error) { command.Reject(error.Code, error.Message); return; }
    if (!(result is IpcDownloadSource source)) { command.Reject("download_invalid_result", "The mod could not prepare the file."); return; }
    if (string.IsNullOrEmpty(BaseUrl)) { source.Dispose(); command.Reject("unavailable", "The download server is unavailable."); return; }
    if (!_tickets.TryCreate(handle.Name, source, handle.Info.AllowedOrigins, out string token, out string code, out string reason))
    { source.Dispose(); command.Reject(code, reason); return; }
    try
    {
      if (command.CancellationToken.IsCancellationRequested) { _tickets.Revoke(token); return; }
      string url = BaseUrl.TrimEnd('/') + "/ipc/download/" + token;
      object payload = metadata == null ? (object)new { url, byteLength = source.ByteLength } : new { url, byteLength = source.ByteLength, metadata };
      command.Reply(eventName, payload);
    }
    catch { _tickets.Revoke(token); throw; }
  }
}
