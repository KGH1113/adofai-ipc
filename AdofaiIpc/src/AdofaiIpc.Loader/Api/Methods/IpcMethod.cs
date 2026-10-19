using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public abstract class IpcMethod
{
  public string Name { get; }
  public string WireName { get; }
  protected IpcMethod(string name, string wireName) { Name = name; WireName = wireName; }
}

public sealed class IpcMethod<TRequest, TResult> : IpcMethod
{
  public IpcCodec<TRequest> Input { get; }
  public IpcCodec<TResult> Output { get; }
  public IpcMethod(string name, string wireName, IpcCodec<TRequest> input, IpcCodec<TResult> output)
    : base(name, wireName) { Input = input; Output = output; }
}
