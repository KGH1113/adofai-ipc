using System;
using System.Collections.Generic;

namespace AdofaiIpc.Core;

internal sealed class IpcDownloadTicketStore
{
  internal const int MaxTicketCount = 128;
  private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(60);

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
          token = DownloadAccess.CreateToken();
        }
        while (_tickets.ContainsKey(token));

        _tickets.Add(
          token,
          new Ticket
          {
            NamespaceName = namespaceName,
            Source = source,
            AllowedOrigins = DownloadAccess.CopyOrigins(allowedOrigins),
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
    bool taken = TryTakeTicket(token, origin, false, out Ticket ticket, out approvedBrowserOrigin, out errorCode, out errorMessage);
    source = ticket?.Source;
    return taken;
  }

  /// <summary>
  /// Takes a ticket whose content can be read again at any position. A source that cannot seek
  /// keeps its ticket and reports <see cref="IpcErrorCodes.FileNotSeekable"/>, so it can still be read once.
  /// </summary>
  internal bool TryTakeSeekable(
    string token,
    string origin,
    out Ticket ticket,
    out string approvedBrowserOrigin,
    out string errorCode,
    out string errorMessage)
    => TryTakeTicket(token, origin, true, out ticket, out approvedBrowserOrigin, out errorCode, out errorMessage);

  private bool TryTakeTicket(
    string token,
    string origin,
    bool seekable,
    out Ticket taken,
    out string approvedBrowserOrigin,
    out string errorCode,
    out string errorMessage)
  {
    approvedBrowserOrigin = null;
    taken = null;
    errorCode = null;
    errorMessage = null;
    Ticket expired = null;

    if (!DownloadAccess.IsValidToken(token))
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
      else if (!DownloadAccess.IsOriginAllowed(ticket.AllowedOrigins, origin, ticket.ExactOrigins))
      {
        errorCode = IpcErrorCodes.OriginNotAllowed;
        errorMessage = "Origin is not allowed for this download ticket.";
      }
      else
      {
        approvedBrowserOrigin = DownloadAccess.ApprovedBrowserOrigin(ticket.AllowedOrigins, origin);
        if (seekable && !(ticket.Source.Content != null && ticket.Source.Content.CanSeek))
        {
          errorCode = IpcErrorCodes.FileNotSeekable;
          errorMessage = "This file can only be read once, from the start. Read it as a stream instead.";
        }
        else
        {
          // Remove before returning the source so the ticket is single-use even
          // if the client disconnects during the stream.
          _tickets.Remove(token);
          taken = ticket;
        }
      }
    }

    expired?.Source.Dispose();
    return taken != null;
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

  internal sealed class Ticket
  {
    public string NamespaceName;
    public IpcDownloadSource Source;
    public string[] AllowedOrigins;
    public bool ExactOrigins;
    public DateTime ExpiresAtUtc;
  }
}
