using AdofaiIpc.Contracts;
using AdofaiIpc.Ports;
namespace AdofaiIpc;

internal static class ModSdkComposition {
  internal static IValueCodec<T> CreateCodec<T>(IpcSchema schema) => new JsonValueCodec<T>(schema);
  internal static ModRegistrationService<TApp> Create<TApp>(IIpcRuntime runtime, ModContract<TApp> contract, string displayName, string[] allowedOrigins) where TApp : IpcApp
    => new ModRegistrationService<TApp>(runtime, contract, displayName, allowedOrigins, new JsonAppProtocol());
}
