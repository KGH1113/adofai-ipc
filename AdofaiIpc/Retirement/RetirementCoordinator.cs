using System;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace AdofaiIpc.Retirement;

internal static class RetirementCoordinator
{
  private static UnityModManager.ModEntry _owner;
  private static DateTime _nextAttempt;
  private static int _attempts;
  private static Task<string> _attempt;
  internal static void Schedule(UnityModManager.ModEntry owner) { _owner = owner; _attempts = 0; _nextAttempt = DateTime.MinValue; }
  internal static void Cancel() { _owner = null; }
  internal static void Tick()
  {
    if (_owner == null) return;
    if (_attempt != null)
    {
      if (!_attempt.IsCompleted) return;
      string result = _attempt.GetAwaiter().GetResult();
      _attempt = null;
      if (result == null) { _owner.Logger.Log("[IpcRetirement] Removed Mods/AdofaiIpc during this game session. Loaded code remains available until exit."); Cancel(); return; }
      if (_attempts++ == 0) _owner.Logger.Log("[IpcRetirement] Waiting for SDK migration: " + result);
      if (_attempts >= 60) { _owner.Logger.Warning("[IpcRetirement] Kept legacy IPC because removal is not ready: " + result); Cancel(); }
      return;
    }
    if (DateTime.UtcNow < _nextAttempt) return;
    _nextAttempt = DateTime.UtcNow.AddSeconds(1);
    var owner = _owner;
    _attempt = Task.Run(() =>
    {
      try
      {
        if (!LegacyRetirement.Ready(owner.Path, out string reason)) return reason;
        LegacyRetirement.Delete(owner.Path);
        return null;
      }
      catch (Exception error) { return "Immediate removal failed: " + error.Message; }
    });
  }
}
