using AdofaiIpc.Ports;
namespace AdofaiIpc;

public sealed class IpcCodec<T> {
  private readonly IValueCodec<T> _codec;
  public IpcCodec(IpcSchema schema) : this(ModSdkComposition.CreateCodec<T>(schema)) { }
  internal IpcCodec(IValueCodec<T> codec) { _codec = codec; }
  public T Read(string json) => _codec.Read(json);
  public string Write(T value) => _codec.Write(value);
  internal string Write(T value, IFileExports files) => _codec.Write(value, files);
}
