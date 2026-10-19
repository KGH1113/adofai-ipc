namespace AdofaiIpc.Core;
internal interface IDownloadTickets {
  bool TryTake(string token, string origin, out IpcDownloadSource source, out string approvedBrowserOrigin, out string code, out string reason);
}
