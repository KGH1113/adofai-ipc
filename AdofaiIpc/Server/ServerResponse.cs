namespace AdofaiIpc.Server;

public sealed class ServerResponse
{
  public int StatusCode { get; }
  public object Body { get; }
  public IpcDownloadSource DownloadSource { get; }
  public bool ApplyCorsHeaders { get; }

  public ServerResponse(int statusCode, object body)
    : this(statusCode, body, true)
  {
  }

  private ServerResponse(int statusCode, object body, bool applyCorsHeaders)
  {
    StatusCode = statusCode;
    Body = body;
    ApplyCorsHeaders = applyCorsHeaders;
  }

  private ServerResponse(int statusCode, IpcDownloadSource downloadSource)
  {
    StatusCode = statusCode;
    DownloadSource = downloadSource;
    ApplyCorsHeaders = false;
  }

  public static ServerResponse Ok(object body) => new ServerResponse(200, body);
  public static ServerResponse NoContent() => new ServerResponse(204, (object)null);
  public static ServerResponse BadRequest(object body) => new ServerResponse(400, body);
  public static ServerResponse NotFound(object body) => new ServerResponse(404, body);
  public static ServerResponse InternalServerError(object body) => new ServerResponse(500, body);
  public static ServerResponse Download(IpcDownloadSource source) => new ServerResponse(200, source);
  public static ServerResponse NoCors(int statusCode, object body) => new ServerResponse(statusCode, body, false);
}
