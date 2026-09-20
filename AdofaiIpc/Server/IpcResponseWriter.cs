using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace AdofaiIpc.Server;

public static class IpcResponseWriter
{
  public static void Write(HttpListenerContext context, ServerResponse result)
  {
    if (result.ApplyCorsHeaders) ApplyHeaders(context);

    context.Response.StatusCode = result.StatusCode;

    if (result.DownloadSource != null)
    {
      WriteDownload(context, result.DownloadSource);
      return;
    }

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

  private static void WriteDownload(HttpListenerContext context, IpcDownloadSource source)
  {
    try
    {
      context.Response.ContentType = source.ContentType;
      context.Response.ContentLength64 = source.ByteLength;
      context.Response.Headers["Content-Disposition"] = BuildContentDisposition(source.FileName);
      context.Response.Headers["Cache-Control"] = "no-store";
      context.Response.Headers["Referrer-Policy"] = "no-referrer";
      context.Response.Headers["X-Content-Type-Options"] = "nosniff";
      source.WriteContent(context.Response.OutputStream);
    }
    catch (HttpListenerException)
    {
      // The client may close the connection while a large stream is in flight.
    }
    catch (InvalidDataException e)
    {
      Main.Instance?.LogException(e);
    }
    catch (IOException e)
    {
      // Input and output streams can both report disconnects as IOException.
      Main.Instance?.LogException(e);
    }
    catch (Exception e)
    {
      Main.Instance?.LogException(e);
    }
    finally
    {
      try
      {
        source.Dispose();
      }
      catch (Exception e)
      {
        Main.Instance?.LogException(e);
      }
      try
      {
        context.Response.OutputStream.Close();
      }
      catch
      {
      }
    }
  }

  public static void WriteError(HttpListenerContext context, Exception error)
  {
    Main.Instance?.LogException(error);
    try
    {
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
    catch (Exception writeError)
    {
      Main.Instance?.LogException(writeError);
    }
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

  private static string BuildContentDisposition(string fileName)
  {
    string fallback = string.IsNullOrEmpty(fileName) ? "download" : fileName;
    StringBuilder safe = new StringBuilder(Math.Min(fallback.Length, 128));

    for (int i = 0; i < fallback.Length && safe.Length < 128; i++)
    {
      char character = fallback[i];
      if (
        character < 0x20 ||
        character == 0x7f ||
        character == '"' ||
        character == '\\' ||
        character == '/')
      {
        safe.Append('_');
      }
      else if (character > 0x7e)
      {
        safe.Append('_');
      }
      else
      {
        safe.Append(character);
      }
    }

    if (safe.Length == 0) safe.Append("download");
    string encoded = Uri.EscapeDataString(fallback).Replace("'", "%27");
    return "attachment; filename=\"" + safe + "\"; filename*=UTF-8''" + encoded;
  }
}
