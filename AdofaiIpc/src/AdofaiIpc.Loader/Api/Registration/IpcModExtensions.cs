using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public static class IpcModExtensions
{
  public static ModRegistration<TApp> RegisterMod<TApp>(this IIpcRuntime ipc, ModContract<TApp> contract,
    string displayName, string[] allowedOrigins) where TApp : IpcApp
    => new ModRegistration<TApp>(ipc, contract, displayName, allowedOrigins);
}
