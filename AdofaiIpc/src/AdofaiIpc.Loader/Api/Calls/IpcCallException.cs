using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;

namespace AdofaiIpc;

public sealed class IpcCallException : Exception
{
  public string Code { get; }
  public IpcCallException(string code, string message) : base(message) { Code = code; }
}
