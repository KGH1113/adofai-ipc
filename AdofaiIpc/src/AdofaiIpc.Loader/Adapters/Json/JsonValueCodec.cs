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

internal sealed class JsonValueCodec<T> : AdofaiIpc.Ports.IValueCodec<T>
{
  private static readonly DefaultContractResolver Resolver = new IpcContractResolver();
  private readonly IpcSchema _schema;
  internal JsonValueCodec(IpcSchema schema) { _schema = schema ?? throw new ArgumentNullException(nameof(schema)); }
  private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings {
    ContractResolver = Resolver, MissingMemberHandling = MissingMemberHandling.Error,
    TypeNameHandling = TypeNameHandling.None, DateParseHandling = DateParseHandling.None, MaxDepth = 32
  });
  public T Read(string json)
  {
    try
    {
      JToken token = IpcJson.Parse(json); IpcSchemaValidator.Validate(_schema, token, JsonValueReader.Instance);
      return typeof(T) == typeof(IpcUnit) ? (T)(object)IpcUnit.Value : token.ToObject<T>(Serializer());
    }
    catch (JsonException error) { throw new IpcValidationException(error.Message); }
  }
  public string Write(T value)
  {
    try
    {
      JToken token = typeof(T) == typeof(IpcUnit) ? (_schema.IsNull ? JValue.CreateNull() : new JObject()) :
        value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer());
      IpcSchemaValidator.Validate(_schema, token, JsonValueReader.Instance); return token.ToString(Formatting.None);
    }
    catch (JsonException error) { throw new IpcValidationException(error.Message); }
  }
}
