using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AdofaiIpc.Core;

internal sealed class IpcDownloadTicketStore
{
  internal const int MaxTicketCount = 128;
  private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);
  private const int TicketBytes = 32;
  private const int MaxTokenLength = 128;

  private readonly object _sync = new object();
  private readonly Dictionary<string, Ticket> _tickets = new Dictionary<string, Ticket>();
  private readonly System.Threading.Timer _expiryTimer;

  internal IpcDownloadTicketStore()
  {
    _expiryTimer = new System.Threading.Timer(_ => PruneExpired(DateTime.UtcNow), null, 30_000, 30_000);
  }

  internal bool TryCreate(
    string namespaceName,
    IpcDownloadSource source,
    string[] allowedOrigins,
    out string token,
    out string errorCode,
    out string errorMessage,
    bool exactOrigins = false)
  {
    token = null;
    errorCode = null;
    errorMessage = null;
    List<Ticket> expired;

    lock (_sync)
    {
      expired = RemoveExpiredLocked(DateTime.UtcNow);
      if (_tickets.Count >= MaxTicketCount)
      {
        errorCode = IpcErrorCodes.DownloadTicketLimit;
        errorMessage = "Too many active download tickets. Try again shortly.";
      }
      else
      {
        do
        {
          token = CreateToken();
        }
        while (_tickets.ContainsKey(token));

        _tickets.Add(
          token,
          new Ticket
          {
            NamespaceName = namespaceName,
            Source = source,
            AllowedOrigins = CopyOrigins(allowedOrigins),
            ExactOrigins = exactOrigins,
            ExpiresAtUtc = DateTime.UtcNow.Add(TicketLifetime)
          });
      }
    }

    DisposeTickets(expired);
    return errorCode == null;
  }

  internal bool TryTake(
    string token,
    string origin,
    out IpcDownloadSource source,
    out string errorCode,
    out string errorMessage)
    => TryTake(token, origin, out source, out _, out errorCode, out errorMessage);

  // The response may grant browser read access only when this ticket carries
  // an explicit mod allowlist. An unrestricted/native ticket gets no CORS grant.
  internal bool TryTake(
    string token,
    string origin,
    out IpcDownloadSource source,
    out string approvedBrowserOrigin,
    out string errorCode,
    out string errorMessage)
  {
    approvedBrowserOrigin = null;
    source = null;
    errorCode = null;
    errorMessage = null;
    Ticket expired = null;

    if (!IsValidToken(token))
    {
      errorCode = IpcErrorCodes.DownloadNotFound;
      errorMessage = "Download ticket not found or expired.";
      return false;
    }

    lock (_sync)
    {
      if (!_tickets.TryGetValue(token, out Ticket ticket))
      {
        errorCode = IpcErrorCodes.DownloadNotFound;
        errorMessage = "Download ticket not found or expired.";
      }
      else if (ticket.ExpiresAtUtc <= DateTime.UtcNow)
      {
        _tickets.Remove(token);
        expired = ticket;
        errorCode = IpcErrorCodes.DownloadNotFound;
        errorMessage = "Download ticket not found or expired.";
      }
      else if (!IsOriginAllowed(ticket.AllowedOrigins, origin, ticket.ExactOrigins))
      {
        errorCode = IpcErrorCodes.OriginNotAllowed;
        errorMessage = "Origin is not allowed for this download ticket.";
      }
      else
      {
        // Remove before returning the source so the ticket is single-use even
        // if the client disconnects during the stream.
        _tickets.Remove(token);
        source = ticket.Source;
        if (ticket.AllowedOrigins != null && ticket.AllowedOrigins.Length > 0
          && Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri)
          && (originUri.Scheme == "http" || originUri.Scheme == "https")
          && origin == originUri.GetLeftPart(UriPartial.Authority))
          approvedBrowserOrigin = originUri.GetLeftPart(UriPartial.Authority);
      }
    }

    expired?.Source.Dispose();
    return source != null;
  }

  internal void Revoke(string token)
  {
    Ticket revoked;
    lock (_sync)
    {
      if (!_tickets.TryGetValue(token, out revoked)) return;
      _tickets.Remove(token);
    }
    revoked.Source.Dispose();
  }

  internal void RevokeNamespace(string namespaceName)
  {
    List<Ticket> revoked = new List<Ticket>();

    lock (_sync)
    {
      List<string> tokens = new List<string>();
      foreach (KeyValuePair<string, Ticket> pair in _tickets)
      {
        if (string.Equals(pair.Value.NamespaceName, namespaceName, StringComparison.Ordinal))
        {
          tokens.Add(pair.Key);
          revoked.Add(pair.Value);
        }
      }

      for (int i = 0; i < tokens.Count; i++) _tickets.Remove(tokens[i]);
    }

    DisposeTickets(revoked);
  }

  internal void Clear()
  {
    List<Ticket> cleared = new List<Ticket>();

    lock (_sync)
    {
      foreach (Ticket ticket in _tickets.Values) cleared.Add(ticket);
      _tickets.Clear();
    }

    DisposeTickets(cleared);
  }

  internal void PruneExpired(DateTime nowUtc)
  {
    List<Ticket> expired;
    lock (_sync)
    {
      expired = RemoveExpiredLocked(nowUtc);
    }

    DisposeTickets(expired);
  }

  private List<Ticket> RemoveExpiredLocked(DateTime now)
  {
    List<Ticket> expired = new List<Ticket>();
    List<string> tokens = new List<string>();

    foreach (KeyValuePair<string, Ticket> pair in _tickets)
    {
      if (pair.Value.ExpiresAtUtc <= now)
      {
        tokens.Add(pair.Key);
        expired.Add(pair.Value);
      }
    }

    for (int i = 0; i < tokens.Count; i++) _tickets.Remove(tokens[i]);
    return expired;
  }

  private static string CreateToken()
  {
    byte[] bytes = new byte[TicketBytes];
    using (RandomNumberGenerator random = RandomNumberGenerator.Create())
    {
      random.GetBytes(bytes);
    }

    return Convert.ToBase64String(bytes)
      .TrimEnd('=')
      .Replace('+', '-')
      .Replace('/', '_');
  }

  private static bool IsValidToken(string token)
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

  private static string[] CopyOrigins(string[] origins)
  {
    if (origins == null || origins.Length == 0) return null;

    string[] copy = new string[origins.Length];
    Array.Copy(origins, copy, origins.Length);
    return copy;
  }

  private static bool IsOriginAllowed(string[] allowedOrigins, string origin, bool exactOrigins)
  {
    if (string.IsNullOrEmpty(origin)) return true;
    if (allowedOrigins == null || allowedOrigins.Length == 0) return true;

    for (int i = 0; i < allowedOrigins.Length; i++)
    {
      if (OriginMatches(allowedOrigins[i], origin, exactOrigins)) return true;
    }

    return false;
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

  private static void DisposeTickets(List<Ticket> tickets)
  {
    if (tickets == null) return;
    for (int i = 0; i < tickets.Count; i++)
    {
      try
      {
        tickets[i].Source.Dispose();
      }
      catch (Exception e)
      {
        System.Diagnostics.Trace.TraceError("Failed to dispose an IPC download source: " + e);
      }
    }
  }

  private sealed class Ticket
  {
    public string NamespaceName;
    public IpcDownloadSource Source;
    public string[] AllowedOrigins;
    public bool ExactOrigins;
    public DateTime ExpiresAtUtc;
  }
}
