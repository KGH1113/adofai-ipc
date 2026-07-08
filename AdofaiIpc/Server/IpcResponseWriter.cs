using System;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace AdofaiIpc.Server;

public static class IpcResponseWriter
{
  public static void Write(HttpListenerContext context, ServerResponse result)
  {
    ApplyHeaders(context);

    context.Response.StatusCode = result.StatusCode;

    if (result.Body == null)
    {
      context.Response.OutputStream.Close();
      return;
    }

    string json = JsonConvert.SerializeObject(result.Body);
    byte[] bytes = Encoding.UTF8.GetBytes(json);

    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.ContentLength64 = bytes.Length;
    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    context.Response.OutputStream.Close();
  }

  public static void WriteError(HttpListenerContext context, Exception error)
  {
    Main.Instance?.LogException(error);
    Write(context, ServerResponse.InternalServerError(new
    {
      ok = false,
      error = new
      {
        code = "internal_error",
        message = "Internal server error."
      }
    }));
  }

  private static void ApplyHeaders(HttpListenerContext context)
  {
    string origin = context.Request.Headers["Origin"];

    if (!string.IsNullOrEmpty(origin))
    {
      context.Response.Headers["Access-Control-Allow-Origin"] = origin;
      context.Response.Headers["Vary"] = "Origin";
    }

    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
  }
}
