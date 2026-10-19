using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace AdofaiIpc;

public sealed class IpcOptionalConverter : JsonConverter
{
  public override bool CanConvert(Type objectType) => objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(IpcOptional<>);
  public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
  {
    Type valueType = objectType.GetGenericArguments()[0];
    object value = serializer.Deserialize(reader, valueType);
    return Activator.CreateInstance(objectType, new[] { value });
  }
  public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
  {
    Type type = value.GetType();
    if (!(bool)type.GetProperty(nameof(IpcOptional<int>.IsSpecified)).GetValue(value))
      throw new JsonSerializationException("An omitted IPC value may only appear as an optional object property.");
    serializer.Serialize(writer, type.GetProperty(nameof(IpcOptional<int>.Value)).GetValue(value));
  }
}
