using System;
namespace AdofaiIpc.Loader;
internal sealed class ProcessRuntimeState : IProcessRuntimeState {
  public object Gate => string.Intern("AdofaiIpc.ProcessRuntime.Gate");
  public object Current { get => AppDomain.CurrentDomain.GetData("AdofaiIpc.ProcessRuntime"); set => AppDomain.CurrentDomain.SetData("AdofaiIpc.ProcessRuntime", value); }
  public string Failure { get => AppDomain.CurrentDomain.GetData("AdofaiIpc.ProcessRuntime.Failure") as string; set => AppDomain.CurrentDomain.SetData("AdofaiIpc.ProcessRuntime.Failure", value); }
}
