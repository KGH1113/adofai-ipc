using AdofaiIpc.Contracts;
namespace AdofaiIpc.Loader;

/// <summary>Selects a compatible locally bundled runtime. Call from the mod's game-thread entry point.</summary>
public static class IpcLoader {
  public static IIpcRuntime GetRuntime(string modsDirectory, RuntimeRequirements requirements = null)
    => RuntimeLoaderComposition.Loader.GetRuntime(modsDirectory, requirements);
}
