using System;
namespace AdofaiIpc.Core;
internal sealed class DownloadManager : IDownloadTickets {
  private readonly IpcDownloadTicketStore _tickets = new IpcDownloadTicketStore();
  internal string BaseUrl { get; set; }
  internal void RevokeNamespace(string name) => _tickets.RevokeNamespace(name);
  internal void Clear() => _tickets.Clear();
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
