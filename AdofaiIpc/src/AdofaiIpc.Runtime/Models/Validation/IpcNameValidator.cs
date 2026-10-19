namespace AdofaiIpc.Core;

public static class IpcNameValidator
{
  public static bool IsValidNamespace(string value)
  {
    return IsValid(value, true);
  }

  public static bool IsValidMethod(string value)
  {
    return IsValid(value, false);
  }

  private static bool IsValid(string value, bool isNamespace)
  {
    if (string.IsNullOrWhiteSpace(value)) return false;

    for (int i = 0; i < value.Length; i++)
    {
      char c = value[i];
      bool valid =
        c >= 'a' && c <= 'z' ||
        c >= '0' && c <= '9' ||
        c == '-' ||
        !isNamespace && c == '.';

      if (!valid) return false;
    }

    return true;
  }
}
