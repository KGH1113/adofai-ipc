using System;
using System.Net;
using System.Threading;

namespace AdofaiIpc.Server;

public sealed class IpcServer
{
  private const int DefaultPort = 32145;
  private const int MaxPort = 32155;

  private readonly HttpListener _listener = new HttpListener();
  private readonly IpcHttpHandler _handler = new IpcHttpHandler();
  private Thread _thread;
  private bool _running;

  public int Port { get; private set; }
  public string Url => Port > 0 ? $"http://127.0.0.1:{Port}/" : null;

  public void Start()
  {
    if (_running) return;

    StartListener();

    _running = true;
    _thread = new Thread(ListenLoop)
    {
      Name = "AdofaiIpc Local HTTP Server",
      IsBackground = true
    };
    _thread.Start();

    Main.Instance?.Log("AdofaiIpc server started: " + Url);
  }

  public void Stop()
  {
    _running = false;

    try
    {
      _listener.Stop();
      _listener.Close();
    }
    catch
    {
    }

    try
    {
      _thread?.Join(500);
    }
    catch
    {
    }

    _thread = null;
    Port = 0;
  }

  private void StartListener()
  {
    Exception lastError = null;

    for (int port = DefaultPort; port <= MaxPort; port++)
    {
      string url = $"http://127.0.0.1:{port}/";

      try
      {
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add(url);
        _listener.Start();

        Port = port;

        if (port != DefaultPort)
        {
          Main.Instance?.Warning(
            $"Default IPC port {DefaultPort} was unavailable. Using fallback port {port}.");
        }

        return;
      }
      catch (HttpListenerException e)
      {
        lastError = e;
      }
      catch (Exception e)
      {
        lastError = e;
        break;
      }
    }

    throw new InvalidOperationException(
      $"Could not start AdofaiIpc server on ports {DefaultPort}-{MaxPort}.",
      lastError);
  }

  private void ListenLoop()
  {
    while (_running)
    {
      try
      {
        HttpListenerContext context = _listener.GetContext();
        Handle(context);
      }
      catch (HttpListenerException)
      {
        return;
      }
      catch (ObjectDisposedException)
      {
        return;
      }
      catch (Exception e)
      {
        Main.Instance?.LogException(e);
      }
    }
  }

  private void Handle(HttpListenerContext context)
  {
    try
    {
      ServerResponse response = _handler.Handle(context);
      IpcResponseWriter.Write(context, response);
    }
    catch (Exception e)
    {
      IpcResponseWriter.WriteError(context, e);
    }
  }
}
