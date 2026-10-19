using System;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Loader;

internal sealed class RuntimeLoader {
  private readonly IRuntimeCatalog _catalog;
  private readonly IRuntimeActivator _activator;
  private readonly IProcessRuntimeState _state;
  internal RuntimeLoader(IRuntimeCatalog catalog, IRuntimeActivator activator, IProcessRuntimeState state) {
    _catalog = catalog; _activator = activator; _state = state;
  }
  internal IIpcRuntime GetRuntime(string modsDirectory, RuntimeRequirements requirements) {
    if (string.IsNullOrWhiteSpace(modsDirectory)) throw new ArgumentException("The Mods directory is required.", nameof(modsDirectory));
    requirements ??= new RuntimeRequirements();
    SemanticVersion.Parse(requirements.MinimumRuntimeVersion);
    lock (_state.Gate) {
      _activator.VerifyContractIdentity();
      object existing = _state.Current;
      if (existing != null) {
        if (!(existing is IIpcRuntime runtime)) throw new InvalidOperationException("The process already selected an incompatible IPC contract.");
        RuntimeSelection.RequireCompatible(runtime.Info, requirements); return runtime;
      }
      if (_state.Failure != null) throw new InvalidOperationException("IPC startup is locked for this process: " + _state.Failure + " Restart the game after correcting the bundled files.");
      RuntimeCandidate selected = RuntimeSelection.Select(_catalog.Discover(modsDirectory));
      if (selected == null) throw new InvalidOperationException("No compatible stable bundled IPC runtime was found under Mods/<mod>/ipc.");
      RuntimeSelection.RequireCompatible(selected.Info, requirements);
      _state.Failure = "startup is in progress";
      try {
        var runtime = _activator.Start(selected);
        _state.Current = runtime; _state.Failure = null; return runtime;
      } catch (Exception error) {
        _state.Failure = error.Message;
        throw new InvalidOperationException("IPC runtime startup failed; game restart is required.", error);
      }
    }
  }
}
