using System;
using System.Collections;
using System.Collections.Generic;

namespace AdofaiIpc.Bootstrap;

internal static class DependencyIssueRegistry
{
  internal const string RegistryKey = "AdofaiIpc.DependencyIssues.v1";
  internal const string RootKey = "uiRoot";
  private const string IssuesKey = "issues";

  public static void Report(DependencyIssue issue)
  {
    lock (AppDomain.CurrentDomain)
    {
      Hashtable root = GetRoot();
      Hashtable issues = (Hashtable)root[IssuesKey];
      issues[issue.ModId] = new Hashtable(StringComparer.Ordinal)
      {
        ["kind"] = issue.Kind.ToString(),
        ["modId"] = issue.ModId,
        ["displayName"] = issue.DisplayName,
        ["minimumVersion"] = issue.MinimumVersion,
        ["installedVersion"] = issue.InstalledVersion ?? string.Empty,
        ["detail"] = issue.Detail ?? string.Empty
      };
    }
  }

  public static List<Hashtable> Snapshot()
  {
    lock (AppDomain.CurrentDomain)
    {
      Hashtable issues = (Hashtable)GetRoot()[IssuesKey];
      List<Hashtable> result = new(issues.Count);
      foreach (DictionaryEntry entry in issues) result.Add((Hashtable)entry.Value);
      result.Sort((left, right) => string.Compare((string)left["displayName"],
        (string)right["displayName"], StringComparison.OrdinalIgnoreCase));
      return result;
    }
  }

  public static Hashtable GetRoot()
  {
    Hashtable root = AppDomain.CurrentDomain.GetData(RegistryKey) as Hashtable;
    if (root != null) return root;
    root = new Hashtable(StringComparer.Ordinal) { [IssuesKey] = new Hashtable(StringComparer.Ordinal) };
    AppDomain.CurrentDomain.SetData(RegistryKey, root);
    return root;
  }
}
