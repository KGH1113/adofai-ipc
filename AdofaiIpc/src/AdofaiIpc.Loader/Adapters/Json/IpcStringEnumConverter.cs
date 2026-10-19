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

/// <summary>Serializes named enum members by their exact EnumMember value; numbers are never accepted.</summary>
public sealed class IpcStringEnumConverter : JsonConverter
{
  private sealed class EnumValues
  {
    internal readonly Dictionary<string, object> Read = new Dictionary<string, object>(StringComparer.Ordinal);
    internal readonly Dictionary<object, string> Write = new Dictionary<object, string>();
    internal EnumValues(Type type)
    {
      foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
      {
        string wire = field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name;
        object value = field.GetValue(null);
        if (Read.ContainsKey(wire) || Write.ContainsKey(value)) throw new JsonSerializationException("Duplicate IPC enum member: " + type.FullName);
        Read.Add(wire, value); Write.Add(value, wire);
      }
    }
  }
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, EnumValues> Values = new System.Collections.Concurrent.ConcurrentDictionary<Type, EnumValues>();
  public override bool CanConvert(Type objectType) => (System.Nullable.GetUnderlyingType(objectType) ?? objectType).IsEnum;
  public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
  {
    Type nullable = System.Nullable.GetUnderlyingType(objectType), type = nullable ?? objectType;
    if (reader.TokenType == JsonToken.Null && nullable != null) return null;
    if (reader.TokenType != JsonToken.String || !Values.GetOrAdd(type, t => new EnumValues(t)).Read.TryGetValue((string)reader.Value, out var result))
      throw new JsonSerializationException("Expected a declared string enum value for " + type.Name + ".");
    return result;
  }
  public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
  {
    if (value == null) { writer.WriteNull(); return; }
    if (!Values.GetOrAdd(value.GetType(), t => new EnumValues(t)).Write.TryGetValue(value, out string wire))
      throw new JsonSerializationException("Unknown IPC enum value.");
    writer.WriteValue(wire);
  }
}
