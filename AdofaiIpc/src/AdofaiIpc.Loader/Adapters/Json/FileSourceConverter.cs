using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AdofaiIpc.Ports;

namespace AdofaiIpc;
internal sealed class FileSourceConverter : JsonConverter, IDisposable
{
  private readonly IFileExports _files;
  private Dictionary<string, IpcFile> _pending;
  internal FileSourceConverter(IFileExports files) { _files = files; }
  public override bool CanConvert(Type type) => type == typeof(IpcFile);
  public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
    => throw new IpcValidationException("Files can only be sent from the mod to an app.");
  public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
  {
    if (_files == null) throw new IpcValidationException("Files must be returned or sent through a registered IPC method.");
    var file = (IpcFile)value;
    string placeholder = "ipc-file:" + Guid.NewGuid().ToString("N");
    (_pending ??= new Dictionary<string, IpcFile>(StringComparer.Ordinal)).Add(placeholder, file);
    writer.WriteStartObject();
    writer.WritePropertyName("url"); writer.WriteValue(placeholder);
    writer.WritePropertyName("byteLength"); writer.WriteValue(file.Size);
    writer.WritePropertyName("fileName"); writer.WriteValue(file.Name);
    writer.WritePropertyName("contentType"); writer.WriteValue(file.ContentType);
    writer.WriteEndObject();
  }
  internal void Complete(JToken token)
  {
    if (_pending == null) return;
    // Validate the complete DTO before exporting. A later export failure must also
    // release sources which have not yet received a transfer lease.
    var urls = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var pending in _pending) urls.Add(pending.Key, _files.Export(pending.Value).Url);
    foreach (var value in ((JContainer)token).Descendants().OfType<JValue>())
      if (value.Type == JTokenType.String && urls.TryGetValue((string)value, out var url)) value.Value = url;
  }
  public void Dispose()
  {
    if (_pending == null) return;
    foreach (var file in _pending.Values)
      try { file.Dispose(); } catch (Exception error) { System.Diagnostics.Trace.TraceError("Failed to dispose an unsent IPC file: " + error); }
  }
}
