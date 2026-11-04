using System;
using System.Security.Cryptography;

namespace AdofaiIpc.Core;

/// <summary>
/// Capability tokens and the browser-origin policy shared by download tickets and opened files.
/// </summary>
internal static class DownloadAccess
{
  private const int TokenBytes = 32;
  private const int MaxTokenLength = 128;

  internal static string CreateToken()
  {
    byte[] bytes = new byte[TokenBytes];
    using (RandomNumberGenerator random = RandomNumberGenerator.Create())
    {
      random.GetBytes(bytes);
    }

    return Convert.ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }

  internal static bool IsValidToken(string token)
  {
    if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return false;

    for (int i = 0; i < token.Length; i++)
    {
      char character = token[i];
      if (
        (character < 'a' || character > 'z') &&
        (character < 'A' || character > 'Z') &&
        (character < '0' || character > '9') &&
        character != '-' &&
        character != '_')
      {
        return false;
      }
    }

    return true;
  }

  internal static string[] CopyOrigins(string[] origins)
  {
    if (origins == null || origins.Length == 0) return null;

    string[] copy = new string[origins.Length];
    Array.Copy(origins, copy, origins.Length);
    return copy;
  }

  /// <summary>
  /// A request without an Origin header (a navigation or a media element) is allowed:
  /// the unguessable token is the capability.
  /// </summary>
  internal static bool IsOriginAllowed(string[] allowedOrigins, string origin, bool exactOrigins)
  {
    if (string.IsNullOrEmpty(origin)) return true;
    if (allowedOrigins == null || allowedOrigins.Length == 0) return true;

    for (int i = 0; i < allowedOrigins.Length; i++)
    {
      if (OriginMatches(allowedOrigins[i], origin, exactOrigins)) return true;
    }

    return false;
  }

  /// <summary>
  /// The origin a response may grant browser read access to. Only a source that carries an
  /// explicit mod allowlist gets a CORS grant; an unrestricted or native source gets none.
  /// Call only after <see cref="IsOriginAllowed"/> accepted the origin.
  /// </summary>
  internal static string ApprovedBrowserOrigin(string[] allowedOrigins, string origin)
  {
    if (allowedOrigins == null || allowedOrigins.Length == 0) return null;
    if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri)) return null;
    if (originUri.Scheme != "http" && originUri.Scheme != "https") return null;

    string authority = originUri.GetLeftPart(UriPartial.Authority);
    return origin == authority ? authority : null;
  }

  private static bool OriginMatches(string allowed, string origin, bool exactOrigins)
  {
    if (string.IsNullOrEmpty(allowed)) return false;
    if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase)) return true;

    if (
      Uri.TryCreate(allowed, UriKind.Absolute, out Uri allowedUri) &&
      Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri))
    {
      return
        string.Equals(allowedUri.Scheme, originUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(allowedUri.Host, originUri.Host, StringComparison.OrdinalIgnoreCase) &&
        (exactOrigins ? allowedUri.Port == originUri.Port : allowedUri.IsDefaultPort);
    }

    return false;
  }
}
