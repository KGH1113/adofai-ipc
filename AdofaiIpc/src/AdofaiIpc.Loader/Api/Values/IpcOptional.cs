using System;
using Newtonsoft.Json;
namespace AdofaiIpc;

/// <summary>A missing property differs from a specified property whose value is null.</summary>
[JsonConverter(typeof(IpcOptionalConverter))]
public readonly struct IpcOptional<T>
{
  private readonly T _value;
  public bool IsSpecified { get; }
  public T Value => IsSpecified ? _value : throw new InvalidOperationException("The IPC property was omitted.");
  public IpcOptional(T value) { _value = value; IsSpecified = true; }
  public static implicit operator IpcOptional<T>(T value) => new IpcOptional<T>(value);
}
