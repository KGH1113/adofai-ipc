using System;

namespace AdofaiIpc.Core;

internal enum ByteRangeKind { Full, Partial, Unsatisfiable }

/// <summary>The part of a file one HTTP request asks for. <see cref="End"/> is exclusive.</summary>
internal readonly struct ByteRange
{
  internal ByteRange(ByteRangeKind kind, long start, long end) { Kind = kind; Start = start; End = end; }
  internal ByteRangeKind Kind { get; }
  internal long Start { get; }
  internal long End { get; }
  internal long Length => End - Start;
}

internal static class ByteRangeRequest
{
  /// <summary>
  /// Reads a single "bytes=" range. Other units, several ranges and malformed values are
  /// ignored as HTTP allows, so the whole file is sent instead.
  /// </summary>
  internal static ByteRange Parse(string header, long length)
  {
    var full = new ByteRange(ByteRangeKind.Full, 0, length);
    var unsatisfiable = new ByteRange(ByteRangeKind.Unsatisfiable, 0, 0);
    if (string.IsNullOrWhiteSpace(header)) return full;

    string value = header.Trim();
    const string unit = "bytes=";
    if (!value.StartsWith(unit, StringComparison.OrdinalIgnoreCase)) return full;
    string spec = value.Substring(unit.Length).Trim();
    if (spec.IndexOf(',') >= 0) return full;

    int dash = spec.IndexOf('-');
    if (dash < 0) return full;
    string first = spec.Substring(0, dash).Trim();
    string last = spec.Substring(dash + 1).Trim();

    if (first.Length == 0)
    {
      // A suffix: the last N bytes.
      if (!TryReadNumber(last, out long suffix)) return full;
      if (suffix == 0 || length == 0) return unsatisfiable;
      return new ByteRange(ByteRangeKind.Partial, Math.Max(0, length - suffix), length);
    }

    if (!TryReadNumber(first, out long start)) return full;
    long end;
    if (last.Length == 0) end = length;
    else
    {
      if (!TryReadNumber(last, out long lastByte) || lastByte < start) return full;
      end = lastByte >= length ? length : lastByte + 1;
    }

    if (start >= length) return unsatisfiable;
    return new ByteRange(ByteRangeKind.Partial, start, end);
  }

  private static bool TryReadNumber(string text, out long value)
  {
    value = 0;
    if (text.Length == 0 || text.Length > 18) return false;
    for (int i = 0; i < text.Length; i++)
    {
      char character = text[i];
      if (character < '0' || character > '9') return false;
      value = value * 10 + (character - '0');
    }
    return true;
  }
}
