using System;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using AdofaiIpc.Core;

namespace AdofaiIpc.Server;

public sealed class IpcHttpHandler
{
  public ServerResponse Handle(HttpListenerContext context)
  {
    string method = context.Request.HttpMethod;
    string path = NormalizePath(context.Request.Url?.AbsolutePath);

    if (method == "OPTIONS") return ServerResponse.NoContent();

    if (method == "GET" && path == "/ipc/health")
    {
      return ServerResponse.Ok(new
      {
        ok = true,
        server = "AdofaiIpc",
        protocolVersion = 1,
        port = Main.Server?.Port ?? 0
      });
    }

    if (method == "GET" && path == "/ipc/namespaces")
    {
      return ServerResponse.Ok(new
      {
        namespaces = AdofaiIpc.Registry.ListNamespaces()
      });
    }

    if (method == "GET" && path.StartsWith("/ipc/namespaces/", StringComparison.Ordinal))
    {
      string namespaceName = Uri.UnescapeDataString(path.Substring("/ipc/namespaces/".Length));
      IpcRegistry.NamespaceDetail detail = AdofaiIpc.Registry.GetNamespace(namespaceName);

      if (detail == null)
      {
        return ServerResponse.NotFound(
          IpcResponse.Fail(null, IpcErrorCodes.NamespaceNotFound, "Namespace not found: " + namespaceName));
      }

      return ServerResponse.Ok(detail);
    }

    if (method == "POST" && path == "/ipc")
    {
      return HandleIpcCall(context);
    }

    return ServerResponse.NotFound(
      IpcResponse.Fail(null, IpcErrorCodes.InvalidRequest, "Endpoint not found."));
  }

  private static ServerResponse HandleIpcCall(HttpListenerContext context)
  {
    IpcRequest request;

    try
    {
      using StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
      string json = reader.ReadToEnd();
      request = JsonConvert.DeserializeObject<IpcRequest>(json);

      string origin = context.Request.Headers["Origin"];

      if (
        IpcNameValidator.IsValidNamespace(request?.Namespace) &&
        !IsOriginAllowed(request.Namespace, origin))
      {
        return new ServerResponse(
          403,
          IpcResponse.Fail(
            request.Id,
            IpcErrorCodes.OriginNotAllowed,
            "Origin is not allowed for namespace: " + request.Namespace));
      }
    }
    catch (Exception e)
    {
      Main.Instance?.LogException(e);
      return ServerResponse.BadRequest(
        IpcResponse.Fail(null, IpcErrorCodes.InvalidRequest, "Request JSON is invalid."));
    }

    IpcResponse response = AdofaiIpc.Registry.Invoke(request);

    if (response.Ok) return ServerResponse.Ok(response);

    int status = response.Error?.Code switch
    {
      IpcErrorCodes.InvalidRequest => 400,
      IpcErrorCodes.InvalidNamespace => 400,
      IpcErrorCodes.InvalidMethod => 400,
      IpcErrorCodes.NamespaceNotFound => 404,
      IpcErrorCodes.HandlerNotFound => 404,
      IpcErrorCodes.HandlerFailed => 500,
      _ => 500
    };

    return new ServerResponse(status, response);
  }

  private static string NormalizePath(string path)
  {
    if (string.IsNullOrEmpty(path)) return "/";
    if (path.Length > 1) return path.TrimEnd('/');
    return path;
  }

  private static bool IsOriginAllowed(string namespaceName, string origin)
  {
    if (string.IsNullOrEmpty(origin)) return true;

    IpcNamespaceInfo info = AdofaiIpc.Registry.GetNamespaceInfo(namespaceName);

    if (info == null) return true;

    if (info.AllowedOrigins == null || info.AllowedOrigins.Length == 0) return true;

    for (int i = 0; i < info.AllowedOrigins.Length; i++)
    {
      if (OriginMatches(info.AllowedOrigins[i], origin)) return true;
    }

    return false;
  }

  private static bool OriginMatches(string allowed, string origin)
  {
    if (string.IsNullOrEmpty(allowed)) return false;

    if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    // 포트는 다를 수 있으니 scheme + host 단위 허용도 지원.
    if (
      Uri.TryCreate(allowed, UriKind.Absolute, out Uri allowedUri) &&
      Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri))
    {
      return
        string.Equals(allowedUri.Scheme, originUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(allowedUri.Host, originUri.Host, StringComparison.OrdinalIgnoreCase) &&
        allowedUri.IsDefaultPort;
    }

    return false;
  }
}
