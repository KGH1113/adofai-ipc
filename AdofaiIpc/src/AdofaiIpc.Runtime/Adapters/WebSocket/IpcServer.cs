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

public sealed partial class IpcServer
{
  private readonly IpcRegistry _registry;
  private readonly DownloadEndpoint _downloads;
  private HttpServer _server;
  private System.Threading.Timer _heartbeat;
  public int Port { get; private set; }
  public string Url => Port == 0 ? null : "http://127.0.0.1:" + Port + "/";
  public IpcServer(IpcRegistry registry) { _registry = registry; _downloads = new DownloadEndpoint(registry, () => Port); }
  public void Start()
  {
    if (_server != null) return;
    _registry.Resume();
    Exception last = null;
    for (int port = 32145; port <= 32155; port++)
    {
      var server = new HttpServer(IPAddress.Loopback, port) { KeepClean = true, WaitTime = TimeSpan.FromSeconds(2) };
      server.Log.Level = LogLevel.Error;
      server.AddWebSocketService<IpcSocket>(IpcProtocol.WebSocketPath, socket => socket.Initialize(_registry));
      server.OnGet += (_, e) => _downloads.Handle(e);
      try { server.Start(); if (!server.IsListening) throw new IOException("IPC port is unavailable."); }
      catch (Exception error) { last = error; server.Stop(); continue; }
      _server = server; Port = port; _registry.DownloadBaseUrl = Url;
      _heartbeat = new System.Threading.Timer(_ => _registry.Heartbeat(), null, 10_000, 10_000);
      return;
    }
    _registry.Suspend();
    throw new InvalidOperationException("Could not start the localhost IPC server.", last);
  }
  public void Stop()
  {
    _registry.Suspend();
    _heartbeat?.Dispose(); _heartbeat = null;
    _server?.Stop(); _server = null; Port = 0;
    _registry.DownloadBaseUrl = null; _registry.ClearDownloadTickets();
  }
}
