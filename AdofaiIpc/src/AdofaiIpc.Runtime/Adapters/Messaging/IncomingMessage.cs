using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Runtime;

internal sealed class IncomingMessage : IIncomingMessage
{
  private readonly FeatureRegistration _registration;
  private readonly IpcCommand _command;
  internal IncomingMessage(FeatureRegistration registration, IpcCommand command)
  {
    _registration = registration; _command = command;
    Client = new ClientConnection(registration, new IpcPeerInfo { PeerId = command.PeerId, ConnectionId = command.ConnectionId, Origin = command.Origin });
  }
  public string Id => _command.Id;
  public string Name => _command.Name;
  public string Json => RuntimeJson.Json(_command.Payload);
  public IClientConnection Client { get; }
  public CancellationToken CancellationToken => _command.CancellationToken;
  public void Reply(string name, string json) => _command.Reply(name, RuntimeJson.Parse(json));
  public void Reject(string code, string message) => _command.Reject(code, message);
  public void Reject(string code, string message, string detailsJson) => _command.Reject(code, message, RuntimeJson.Parse(detailsJson));
  public void ReplyDownload(string name, Stream source, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null)
  {
    IpcDownloadSource download;
    try { download = new IpcDownloadSource(source, byteLength, fileName, contentType); }
    catch { source?.Dispose(); throw; }
    SendDownload(name, download, metadataJson);
  }
  public void ReplyDownload(string name, Action<Stream> writer, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null)
    => SendDownload(name, new IpcDownloadSource(writer, byteLength, fileName, contentType), metadataJson);
  private void SendDownload(string name, IpcDownloadSource source, string metadataJson)
  {
    try { _registration.Check(); _registration.Handle.ReplyDownload(_command, name, source, metadataJson == null ? null : RuntimeJson.Parse(metadataJson)); }
    catch { source.Dispose(); throw; }
  }
}
