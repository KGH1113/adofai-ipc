# Dependency Bootstrap in-game E2E

Use disposable copies of the ADOFAI `Mods` directory. Fully quit ADOFAI between cases; do not
replace a DLL while Unity is running. For two-dependent-mod cases, install the shim-based releases
of both TUFReplay and TUFHelperLite.

1. Remove `Mods/AdofaiIpc`, keep network access, and start the game. Verify one download/install,
   both dependent cores loaded, and no `[AdofaiIpc] Dependency Error UI v1` object.
2. Remove AdofaiIpc and block the release URL. Verify one dialog, both mod names, both minimum
   versions, download button, and neither dependent core loaded.
3. Install AdofaiIpc and explicitly disable it in UMM. Verify the setting remains disabled, one
   dialog is shown, and both cores are not loaded.
4. Install AdofaiIpc 0.1.x. Verify one outdated dialog containing the installed version and highest
   required minimum version.
5. Corrupt `AdofaiIpc.dll`. Verify the load-failure message references the UMM log and neither core
   is called.
6. Put bootstrap 0.3.0 in one dependent mod and a newer compatible bootstrap candidate in the
   other. Trigger errors and verify exactly one named root and one Canvas exist.
7. Run case 2 in Korean and English. Verify localized copy, ADOFAI's localized font, working download
   button, and close behavior. Report another issue after closing and verify content updates without
   reopening.
8. Start with bootstrap B1, let a mod update stage B2, and inspect `state.json`. Verify B1 remains
   loaded during that process and B2 is promoted on the next game start.
9. Replace the B2 trial with a damaged assembly. Verify the shim clears Trial and immediately calls
   B1 without changing the dependent runtime.
10. Start a normal installation and inspect the Unity hierarchy and profiler. Verify no dependency
    error root, scene callback, coroutine, `Update`, `OnGUI`, or recurring allocation exists.

Keep the UMM log, the two dependency `state.json` files, screenshots of the hierarchy/dialog, and a
profiler capture with each test run.
