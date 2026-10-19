using System.Collections.Generic;
using AdofaiIpc.Contracts;
namespace AdofaiIpc.Loader;
internal interface IRuntimeCatalog { IEnumerable<RuntimeCandidate> Discover(string modsDirectory); }
internal interface IRuntimeActivator {
  void VerifyContractIdentity();
  IIpcRuntime Start(RuntimeCandidate selected);
}
internal interface IProcessRuntimeState {
  object Gate { get; }
  object Current { get; set; }
  string Failure { get; set; }
}
