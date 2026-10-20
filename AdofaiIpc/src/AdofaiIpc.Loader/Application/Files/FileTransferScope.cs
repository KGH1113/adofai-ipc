using System;
using System.Collections.Generic;
using System.Threading;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

/// <summary>Roll back partially serialized or undelivered transfers.</summary>
internal sealed class FileTransferScope : IDisposable, Ports.IFileExports
{
  private readonly IFeatureDownloads _downloads;
  private readonly CancellationToken _cancellation;
  private readonly CancellationToken _callCancellation;
  private readonly List<IpcFileTicket> _tickets = new List<IpcFileTicket>();
  private bool _committed;
  internal FileTransferScope(IFeatureRegistration registration, CancellationToken cancellation, CancellationToken callCancellation)
  { _downloads = registration as IFeatureDownloads; _cancellation = cancellation; _callCancellation = callCancellation; }
  public IpcFileTicket Export(IpcFile file)
  {
    var ticket = file.Export(_downloads, _cancellation, _callCancellation);
    _tickets.Add(ticket); return ticket;
  }
  internal void Commit() => _committed = true;
  public void Dispose()
  {
    if (_committed) return;
    foreach (var ticket in _tickets)
      try { ticket.Dispose(); } catch (Exception error) { System.Diagnostics.Trace.TraceError("Failed to revoke an IPC file: " + error); }
  }
}
