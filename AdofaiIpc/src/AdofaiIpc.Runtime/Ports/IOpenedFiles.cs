using System;

namespace AdofaiIpc.Core;

/// <summary>Files an app opened for repeated reads at any position.</summary>
internal interface IOpenedFiles
{
  /// <summary>Turns a download ticket into an opened file and returns the opened file's URL.</summary>
  bool TryOpen(string downloadToken, string origin, out string url, out string approvedBrowserOrigin, out string code, out string reason);
  bool TryRead(string token, string origin, out IOpenedFileRead read, out string approvedBrowserOrigin, out string code, out string reason);
  bool TryAuthorize(string token, string origin, out string approvedBrowserOrigin, out string code, out string reason);
  bool TryClose(string token, string origin, out string approvedBrowserOrigin, out string code, out string reason);
}

/// <summary>One request's access to an opened file. Disposing it ends the request.</summary>
internal interface IOpenedFileRead : IDisposable
{
  long ByteLength { get; }
  string FileName { get; }
  string ContentType { get; }
  /// <summary>Reads up to <paramref name="count"/> bytes starting at <paramref name="position"/>.</summary>
  int ReadAt(long position, byte[] buffer, int offset, int count);
}
