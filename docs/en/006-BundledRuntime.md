# Bundled runtime and version contract

[Guide](000-DevelopGuide.md) | [한국어](../kor/006-BundledRuntime.md)

Each mod ships the same fixed layout described in Getting Started. The loader scans only
Mods/<mod>/ipc/manifest.json and inspects DLL metadata without loading candidates. Disabled
mods' bundles are candidates too, so enabling order does not select an older runtime.
Only the chosen DLL is loaded. Invalid candidates are skipped with a Trace diagnostic.

Selection uses manifest schema 1, contract major 1, contract assembly identity 1.0.0.0 and wire
major 3. Among matching candidates the highest stable SemVer wins. Prerelease candidates are
never automatically selected. Equal versions use the ordinal full path for a deterministic tie.
Callers declare a minimum runtime version, contract/wire majors and required capabilities.
A requirement failure does not start or poison a host; other compatible consumers can proceed.

The manifest is shipped beside AdofaiIpc.Runtime.dll:

```json
{
  "SchemaVersion": 1,
  "RuntimeVersion": "2.0.0",
  "ContractMajor": 1,
  "ContractAssemblyVersion": "1.0.0.0",
  "WireMajor": 3,
  "Capabilities": ["commands", "game-thread", "snapshots", "local-peer", "downloads"]
}
```

Before startup the loader checks the runtime's contract assembly reference and compares Describe()
against the manifest. It also verifies the returned runtime's metadata. Once a DLL is loaded,
startup failure is retained until game restart; it never falls back to another loaded version.
Do not alter manifests independently from their DLLs.

Coordination uses one AppDomain slot and a process-shared interned lock, rather than per-loader
static state. Contract references must resolve to the same Assembly instance. Duplicate contract
assemblies and runtime DLLs loaded before selection are rejected explicitly. Do not load contract
copies with Assembly.Load(byte[]), merge them into consumer assemblies, or directly reference Runtime.
This supports the game's single scripting AppDomain; independent AppDomains are not supported.

After selection, every consumer gets the same runtime. A newly installed higher version requires
a full game restart. A late consumer that requires a higher minimum or missing capability fails
its own setup while existing registrations and connections continue. No installer, downloader,
UMM dependency activation, self-update, trial/rollback state or migration runs in v2.

Release rules:

- Contracts and Loader use AssemblyVersion 1.0.0.0 throughout this contract major. FileVersion and
  InformationalVersion identify product releases. Publish the same contract ABI in every bundle.
- Within major 1, retain public members and signatures. Do not add required members to existing
  interfaces. Use separate extension interfaces and capability negotiation for future additions.
- Breaking API changes require a new contract major and identity, with an explicit new selection
  policy. This loader deliberately supports only major 1; it does not host incompatible majors together.
- Wire changes and namespace feature message changes have their own major versions. The namespace
  lifecycle payload exposes featureProtocolMajor; domain clients decide compatibility before commands.
- Validate old compiled consumer adapters against new runtime releases, both load orders, disable/
  re-enable, cancellation and reconnect. CLR tests alone do not establish Unity/Mono compatibility.

Before releasing, run the two-consumer scenarios in the actual game. Check that the installed UMM
resolves root Contracts/Loader references to one contract instance, that startup runs on Unity's
synchronization context, and that quitting/unloading closes the host. Automated tests use .NET 8;
they do not launch ADOFAI or assert behavior of its native Mono loader.
