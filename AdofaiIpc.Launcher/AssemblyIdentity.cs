using System;
using System.IO;
using System.Reflection;

namespace AdofaiIpc.Launcher;

internal static class AssemblyIdentity
{
  internal static Assembly LoadExact(string path)
  {
    string expected = Path.GetFullPath(path);
    AssemblyName identity = AssemblyName.GetAssemblyName(expected);
    Assembly loaded = Assembly.LoadFrom(expected);
    if (loaded.FullName != identity.FullName ||
        (!string.Equals(Path.GetFullPath(loaded.Location), expected, StringComparison.OrdinalIgnoreCase) &&
         !SameContents(expected, loaded.Location)))
      throw new FileLoadException("Mono reused a different IPC assembly. Requested " + identity.FullName +
        " at " + expected + "; loaded " + loaded.FullName + " at " + loaded.Location);
    return loaded;
  }

  private static bool SameContents(string expected, string actual)
  {
    // Mono resolves symlinks in Location (e.g. macOS /var -> /private/var).
    // Also permit an already loaded, byte-identical compatibility copy.
    using FileStream left = File.OpenRead(expected);
    using FileStream right = File.OpenRead(actual);
    if (left.Length != right.Length) return false;
    byte[] a = new byte[8192], b = new byte[8192];
    int read;
    while ((read = left.Read(a, 0, a.Length)) != 0)
    {
      int offset = 0;
      while (offset < read)
      {
        int count = right.Read(b, offset, read - offset);
        if (count == 0) return false;
        offset += count;
      }
      for (int i = 0; i < read; i++) if (a[i] != b[i]) return false;
    }
    return true;
  }
}
