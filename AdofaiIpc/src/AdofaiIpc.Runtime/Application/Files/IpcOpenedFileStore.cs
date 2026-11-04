using System;
using System.Collections.Generic;
using System.IO;

namespace AdofaiIpc.Core;

/// <summary>
/// Files opened for repeated reads at any position, such as a video the browser plays and seeks.
/// Each keeps its seekable source until the app closes it, its session or mod ends,
/// or nothing has read it for <see cref="IdleLifetime"/>.
/// </summary>
internal sealed class IpcOpenedFileStore
{
  internal const int MaxOpenFiles = 64;
  internal static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);

  private readonly object _sync = new object();
  private readonly Dictionary<string, OpenedFile> _files = new Dictionary<string, OpenedFile>();
  private readonly System.Threading.Timer _expiryTimer;

  internal IpcOpenedFileStore()
  {
    _expiryTimer = new System.Threading.Timer(_ => PruneIdle(DateTime.UtcNow), null, 30_000, 30_000);
  }

  internal bool HasCapacity
  {
    get { lock (_sync) return _files.Count < MaxOpenFiles; }
  }

  internal bool TryOpen(IpcDownloadTicketStore.Ticket ticket, out string token, out string errorCode, out string errorMessage)
  {
    token = null;
    errorCode = null;
    errorMessage = null;

    lock (_sync)
    {
      if (_files.Count >= MaxOpenFiles)
      {
        errorCode = IpcErrorCodes.FileOpenLimit;
        errorMessage = "Too many files are open. Close one and try again.";
        return false;
      }

      do
      {
        token = DownloadAccess.CreateToken();
      }
      while (_files.ContainsKey(token));

      _files.Add(token, new OpenedFile(ticket, DateTime.UtcNow));
    }

    return true;
  }

  internal bool TryRead(string token, string origin, out IOpenedFileRead read, out string approvedBrowserOrigin, out string errorCode, out string errorMessage)
  {
    read = null;
    if (!TryFind(token, origin, true, out OpenedFile file, out approvedBrowserOrigin, out errorCode, out errorMessage)) return false;
    read = new Read(this, file);
    return true;
  }

  internal bool TryAuthorize(string token, string origin, out string approvedBrowserOrigin, out string errorCode, out string errorMessage)
    => TryFind(token, origin, false, out _, out approvedBrowserOrigin, out errorCode, out errorMessage);

  internal bool TryClose(string token, string origin, out string approvedBrowserOrigin, out string errorCode, out string errorMessage)
  {
    OpenedFile closed = null;
    approvedBrowserOrigin = null;
    errorCode = null;
    errorMessage = null;

    lock (_sync)
    {
      if (!FindLocked(token, origin, out OpenedFile file, out approvedBrowserOrigin, out errorCode, out errorMessage)) return false;
      _files.Remove(token);
      closed = file;
    }

    closed.Close();
    return true;
  }

  internal void RevokeSource(IpcDownloadSource source)
    => RemoveWhere(file => ReferenceEquals(file.Source, source));

  internal void RevokeNamespace(string namespaceName)
    => RemoveWhere(file => string.Equals(file.NamespaceName, namespaceName, StringComparison.Ordinal));

  internal void Clear() => RemoveWhere(_ => true);

  internal void PruneIdle(DateTime nowUtc)
    => RemoveWhere(file => file.ActiveReads == 0 && file.LastUsedUtc + IdleLifetime <= nowUtc);

  private bool TryFind(string token, string origin, bool beginRead, out OpenedFile file, out string approvedBrowserOrigin, out string errorCode, out string errorMessage)
  {
    lock (_sync)
    {
      if (!FindLocked(token, origin, out file, out approvedBrowserOrigin, out errorCode, out errorMessage)) return false;
      file.LastUsedUtc = DateTime.UtcNow;
      if (beginRead) file.ActiveReads++;
      return true;
    }
  }

  private bool FindLocked(string token, string origin, out OpenedFile file, out string approvedBrowserOrigin, out string errorCode, out string errorMessage)
  {
    approvedBrowserOrigin = null;
    errorCode = null;
    errorMessage = null;

    if (!DownloadAccess.IsValidToken(token) || !_files.TryGetValue(token, out file))
    {
      file = null;
      errorCode = IpcErrorCodes.FileExpired;
      errorMessage = "The opened file was closed or expired. Request the file again.";
      return false;
    }

    if (!DownloadAccess.IsOriginAllowed(file.AllowedOrigins, origin, file.ExactOrigins))
    {
      file = null;
      errorCode = IpcErrorCodes.OriginNotAllowed;
      errorMessage = "Origin is not allowed for this file.";
      return false;
    }

    approvedBrowserOrigin = DownloadAccess.ApprovedBrowserOrigin(file.AllowedOrigins, origin);
    return true;
  }

  private void EndRead(OpenedFile file)
  {
    lock (_sync)
    {
      if (file.ActiveReads > 0) file.ActiveReads--;
      file.LastUsedUtc = DateTime.UtcNow;
    }
  }

  private void RemoveWhere(Func<OpenedFile, bool> predicate)
  {
    List<OpenedFile> removed = new List<OpenedFile>();

    lock (_sync)
    {
      // Every file source's lifetime ends here; most were never opened.
      if (_files.Count == 0) return;
      List<string> tokens = new List<string>();
      foreach (KeyValuePair<string, OpenedFile> pair in _files)
      {
        if (!predicate(pair.Value)) continue;
        tokens.Add(pair.Key);
        removed.Add(pair.Value);
      }

      for (int i = 0; i < tokens.Count; i++) _files.Remove(tokens[i]);
    }

    // Closing disposes the source, whose lifetime callback may come back to this store.
    // Never hold the store lock while doing it.
    for (int i = 0; i < removed.Count; i++) removed[i].Close();
  }

  private sealed class OpenedFile
  {
    private readonly object _gate = new object();
    private bool _closed;

    internal OpenedFile(IpcDownloadTicketStore.Ticket ticket, DateTime nowUtc)
    {
      Source = ticket.Source;
      NamespaceName = ticket.NamespaceName;
      AllowedOrigins = ticket.AllowedOrigins;
      ExactOrigins = ticket.ExactOrigins;
      LastUsedUtc = nowUtc;
    }

    internal IpcDownloadSource Source { get; }
    internal string NamespaceName { get; }
    internal string[] AllowedOrigins { get; }
    internal bool ExactOrigins { get; }
    internal DateTime LastUsedUtc { get; set; }
    internal int ActiveReads { get; set; }

    /// <summary>Requests share one stream, so each read seeks and reads under one lock.</summary>
    internal int ReadAt(long position, byte[] buffer, int offset, int count)
    {
      lock (_gate)
      {
        if (_closed) throw new ObjectDisposedException("The opened file was closed.");
        Stream content = Source.Content;
        content.Seek(position, SeekOrigin.Begin);
        return content.Read(buffer, offset, count);
      }
    }

    internal void Close()
    {
      lock (_gate)
      {
        if (_closed) return;
        _closed = true;
      }

      try
      {
        Source.Dispose();
      }
      catch (Exception e)
      {
        System.Diagnostics.Trace.TraceError("Failed to dispose an opened IPC file: " + e);
      }
    }
  }

  private sealed class Read : IOpenedFileRead
  {
    private readonly IpcOpenedFileStore _store;
    private readonly OpenedFile _file;
    private int _disposed;

    internal Read(IpcOpenedFileStore store, OpenedFile file)
    {
      _store = store;
      _file = file;
    }

    public long ByteLength => _file.Source.ByteLength;
    public string FileName => _file.Source.FileName;
    public string ContentType => _file.Source.ContentType;
    public int ReadAt(long position, byte[] buffer, int offset, int count) => _file.ReadAt(position, buffer, offset, count);

    public void Dispose()
    {
      if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0) _store.EndRead(_file);
    }
  }
}
