using System;
using System.Collections.Generic;
using AdofaiIpc.Ports;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AdofaiIpc;

internal sealed class JsonValueReader : IValueReader<JToken> {
  internal static readonly JsonValueReader Instance = new JsonValueReader();
  public IpcValueKind Kind(JToken value) => value?.Type switch {
    JTokenType.Null => IpcValueKind.Null, JTokenType.String => IpcValueKind.String,
    JTokenType.Boolean => IpcValueKind.Boolean, JTokenType.Integer => IpcValueKind.Integer,
    JTokenType.Float => IpcValueKind.Float, JTokenType.Array => IpcValueKind.Array,
    JTokenType.Object => IpcValueKind.Object, _ => IpcValueKind.Missing
  };
  public string Text(JToken value) => value.ToString(Formatting.None);
  public string String(JToken value) => (string)value;
  public int Count(JToken array) => ((JArray)array).Count;
  public JToken Item(JToken array, int index) => array[index];
  public IEnumerable<string> Keys(JToken obj) { foreach (var property in ((JObject)obj).Properties()) yield return property.Name; }
  public bool TryGet(JToken obj, string name, out JToken value) => ((JObject)obj).TryGetValue(name, StringComparison.Ordinal, out value);
}
