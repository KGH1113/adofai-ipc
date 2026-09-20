using System;

namespace AdofaiIpc;

/// <summary>
/// Domain-level failure returned by a download handler.
/// </summary>
public sealed class IpcDownloadError
{
  public string Code { get; }
  public string Message { get; }
  public int StatusCode { get; }

  public IpcDownloadError(string code, string message, int statusCode = 400)
  {
    if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("An error code is required.", nameof(code));
    if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An error message is required.", nameof(message));
    if (statusCode < 400 || statusCode > 599)
    {
      throw new ArgumentOutOfRangeException(nameof(statusCode), "The status code must be between 400 and 599.");
    }

    Code = code;
    Message = message;
    StatusCode = statusCode;
  }
}
