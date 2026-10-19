using System.Collections.Generic;
namespace AdofaiIpc.Ports;
internal enum IpcValueKind { Missing, Null, String, Boolean, Integer, Float, Array, Object }
internal interface IValueReader<T> {
  IpcValueKind Kind(T value);
  string Text(T value);
  string String(T value);
  int Count(T array);
  T Item(T array, int index);
  IEnumerable<string> Keys(T obj);
  bool TryGet(T obj, string name, out T value);
}
