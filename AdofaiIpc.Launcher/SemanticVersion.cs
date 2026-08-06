using System;
using System.Linq;

namespace AdofaiIpc.Launcher;

internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
  private readonly int[] _numbers;
  private readonly string[] _prerelease;

  private SemanticVersion(int[] numbers, string[] prerelease)
  {
    _numbers = numbers;
    _prerelease = prerelease;
  }

  public bool IsPrerelease => _prerelease.Length != 0;

  public static SemanticVersion Parse(string value)
  {
    if (!TryParse(value, out SemanticVersion version))
      throw new FormatException("Invalid semantic version: " + value);
    return version;
  }

  public static bool TryParse(string value, out SemanticVersion version)
  {
    version = null;
    if (string.IsNullOrWhiteSpace(value)) return false;
    string normalized = value.Trim();
    if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)) normalized = normalized.Substring(1);
    int build = normalized.IndexOf('+');
    if (build >= 0) normalized = normalized.Substring(0, build);
    string[] pair = normalized.Split(new[] { '-' }, 2);
    string[] numeric = pair[0].Split('.');
    if (numeric.Length < 2 || numeric.Length > 4) return false;
    int[] numbers = new int[Math.Max(3, numeric.Length)];
    for (int index = 0; index < numeric.Length; index++)
      if (!int.TryParse(numeric[index], out numbers[index]) || numbers[index] < 0) return false;
    string[] prerelease = pair.Length == 2 ? pair[1].Split('.') : Array.Empty<string>();
    if (prerelease.Any(string.IsNullOrWhiteSpace)) return false;
    version = new SemanticVersion(numbers, prerelease);
    return true;
  }

  public int CompareTo(SemanticVersion other)
  {
    if (other == null) return 1;
    int length = Math.Max(_numbers.Length, other._numbers.Length);
    for (int index = 0; index < length; index++)
    {
      int left = index < _numbers.Length ? _numbers[index] : 0;
      int right = index < other._numbers.Length ? other._numbers[index] : 0;
      int comparison = left.CompareTo(right);
      if (comparison != 0) return comparison;
    }
    if (!IsPrerelease && other.IsPrerelease) return 1;
    if (IsPrerelease && !other.IsPrerelease) return -1;
    for (int index = 0; index < Math.Max(_prerelease.Length, other._prerelease.Length); index++)
    {
      if (index >= _prerelease.Length) return -1;
      if (index >= other._prerelease.Length) return 1;
      bool leftNumeric = int.TryParse(_prerelease[index], out int left);
      bool rightNumeric = int.TryParse(other._prerelease[index], out int right);
      if (leftNumeric && rightNumeric)
      {
        int comparison = left.CompareTo(right);
        if (comparison != 0) return comparison;
      }
      else if (leftNumeric != rightNumeric)
      {
        return leftNumeric ? -1 : 1;
      }
      else
      {
        int comparison = string.CompareOrdinal(_prerelease[index], other._prerelease[index]);
        if (comparison != 0) return comparison;
      }
    }
    return 0;
  }
}
