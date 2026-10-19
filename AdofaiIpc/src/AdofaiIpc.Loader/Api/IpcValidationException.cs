using System;
namespace AdofaiIpc;

public sealed class IpcValidationException : ArgumentException
{
  public IpcValidationException(string message) : base(message) { }
}
