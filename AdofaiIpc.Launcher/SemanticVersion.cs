using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AdofaiIpc.Launcher;

internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
  private static readonly Regex Pattern = new(
    @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
    RegexOptions.CultureInvariant);
  private readonly string[] _numbers;
  private readonly string[] _prerelease;
  public string Value { get; }
  public bool IsPrerelease => _prerelease.Length != 0;

  private SemanticVersion(string value, string[] numbers, string[] prerelease)
  { Value = value; _numbers = numbers; _prerelease = prerelease; }

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
    Match match = Pattern.Match(value);
    if (!match.Success) return false;
    string[] numbers = { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value };
    string[] prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
    if (prerelease.Any(part => part.Length == 0 || part.All(char.IsDigit) && part.Length > 1 && part[0] == '0')) return false;
    version = new SemanticVersion(value, numbers, prerelease);
    return true;
  }

  public int CompareTo(SemanticVersion other)
  {
    if (other == null) return 1;
    for (int index = 0; index < 3; index++)
    { int result = CompareNumeric(_numbers[index], other._numbers[index]); if (result != 0) return result; }
    if (!IsPrerelease && other.IsPrerelease) return 1;
    if (IsPrerelease && !other.IsPrerelease) return -1;
    for (int index = 0; index < Math.Max(_prerelease.Length, other._prerelease.Length); index++)
    {
      if (index >= _prerelease.Length) return -1;
      if (index >= other._prerelease.Length) return 1;
      bool leftNumeric = _prerelease[index].All(char.IsDigit);
      bool rightNumeric = other._prerelease[index].All(char.IsDigit);
      if (leftNumeric && rightNumeric)
      { int result = CompareNumeric(_prerelease[index], other._prerelease[index]); if (result != 0) return result; }
      else if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
      else { int result = string.CompareOrdinal(_prerelease[index], other._prerelease[index]); if (result != 0) return result; }
    }
    return 0;
  }

  private static int CompareNumeric(string left, string right)
  {
    int length = left.Length.CompareTo(right.Length);
    return length != 0 ? length : string.CompareOrdinal(left, right);
  }
}
