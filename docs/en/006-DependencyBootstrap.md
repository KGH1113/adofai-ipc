# Dependency Bootstrap

Dependent mods package a fixed `AdofaiIpc.DependencyShim.dll`, a three-field
`AdofaiIpcBootstrap.json`, and a versioned bootstrap candidate:

```text
AdofaiIpc.DependencyShim.dll
AdofaiIpcBootstrap.json
DependencyBootstrap/state.json
DependencyBootstrap/versions/0.4.1/AdofaiIpc.Bootstrap.dll
```

`Info.json` points to `AdofaiIpc.DependencyShim.DependencyShim.Load`. The manifest declares only
`MinimumAdofaiIpcVersion`, `AssemblyName`, and `EntryMethod`. Legacy download URL fields are accepted
but ignored; official release URLs are owned by the bootstrap.

The shim tries `Trial` first and promotes it after a successful bootstrap entry call. A thrown
exception or `false` result falls back to `Current`, then `Previous`. Updaters call
`StageCandidate(modRoot, assemblyPath)` only after verifying their complete release package and call
`DiscardTrial(modRoot, version)` when the corresponding mod runtime is rolled back. Loaded assemblies
are never overwritten; the trial becomes active on the next game start.

The bootstrap blocks the dependent core when AdofaiIpc is disabled, outdated, or cannot load. A
missing installation is attempted once per process. If that fails, or another dependency error is
found, all affected mods are aggregated into one retained Unity uGUI dialog. The success path creates
no GameObject, event subscription, coroutine, or per-frame callback.

The first bridge releases use the shared `AdofaiIpc.Migration.dll`. They install the fixed shim and
bootstrap, stop the dependent core for that session, and show one dialog listing every affected mod.
The user reinstalls only AdofaiIpc, fully quits the game, and starts it again. New installations use
the final layout directly and do not need this transition.
