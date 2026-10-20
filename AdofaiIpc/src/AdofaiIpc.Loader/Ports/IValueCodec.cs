namespace AdofaiIpc.Ports;
internal interface IValueCodec<T> {
  T Read(string json);
  string Write(T value, IFileExports files = null);
}
