using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public abstract class ModContract<TApp> where TApp : IpcApp
{
  public string Id { get; }
  public string Hash { get; }
  internal IpcMethod[] Methods { get; }
  internal Func<IpcAppInvoker, TApp> AppFactory { get; }
  protected ModContract(string id, string hash, IpcMethod[] methods, Func<IpcAppInvoker, TApp> appFactory)
  {
    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(hash)) throw new ArgumentException("A contract identity is required.");
    Id = id; Hash = hash; Methods = (IpcMethod[])methods.Clone(); AppFactory = appFactory;
  }
}
