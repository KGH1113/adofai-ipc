using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AdofaiIpc.Contracts;

namespace AdofaiIpc.Loader;

internal sealed class RuntimeActivator : IRuntimeActivator {
  public IIpcRuntime Start(RuntimeCandidate selected) {
    try {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "AdofaiIpc.Runtime"))
          throw new InvalidOperationException("A runtime DLL was loaded before selection. Consumer mods must reference only Contracts and Loader.");
        Assembly assembly = Assembly.LoadFrom(selected.AssemblyPath);
        var contract = assembly.GetReferencedAssemblies().SingleOrDefault(name => name.Name == "AdofaiIpc.Contracts");
        if (contract == null || contract.FullName != typeof(IIpcRuntime).Assembly.GetName().FullName)
          throw new InvalidDataException("The runtime references a different contract assembly identity.");
        Type entry = assembly.GetType("AdofaiIpc.Runtime.RuntimeEntry", true);
        MethodInfo describe = entry.GetMethod("Describe", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        MethodInfo start = entry.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (describe == null || start == null) throw new InvalidDataException("The runtime entry contract is missing.");
        selected.Verify(describe.Invoke(null, null) as RuntimeInfo);
        var runtime = start.Invoke(null, null) as IIpcRuntime ?? throw new InvalidDataException("The runtime did not return the shared contract.");
        selected.Verify(runtime.Info);
        return runtime;
    } catch (TargetInvocationException invocation) when (invocation.InnerException != null) {
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(invocation.InnerException).Throw();
      throw;
    }
  }
  public void VerifyContractIdentity()
  {
    Assembly own = typeof(IIpcRuntime).Assembly;
    if (own.GetName().Version.ToString() != IpcContract.AssemblyVersion ||
      AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "AdofaiIpc.Contracts" && !ReferenceEquals(assembly, own)))
      throw new InvalidOperationException("IPC contract DLLs did not resolve to one shared assembly. Do not load contract copies with Assembly.Load(byte[]).");
  }

}
