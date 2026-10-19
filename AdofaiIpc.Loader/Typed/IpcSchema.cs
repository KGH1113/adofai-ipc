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

public sealed class IpcValidationException : ArgumentException
{
  public IpcValidationException(string message) : base(message) { }
}

/// <summary>Immutable wire schema emitted from the TypeSpec contract.</summary>
public sealed class IpcSchema
{
  private readonly string _kind;
  private readonly Dictionary<string, IpcSchema> _fields;
  private readonly IpcSchema _element;
  private readonly HashSet<string> _values;
  private IpcSchema(string kind, Dictionary<string, IpcSchema> fields = null, IpcSchema element = null, HashSet<string> values = null)
  { _kind = kind; _fields = fields; _element = element; _values = values; }
  public static readonly IpcSchema String = new IpcSchema("string");
  public static readonly IpcSchema Boolean = new IpcSchema("boolean");
  public static readonly IpcSchema Int32 = new IpcSchema("int32");
  public static readonly IpcSchema Float64 = new IpcSchema("float64");
  public static readonly IpcSchema SafeInt = new IpcSchema("safeint");
  public static readonly IpcSchema Null = new IpcSchema("null");
  internal bool IsNull => _kind == "null";
  public static IpcSchema Array(IpcSchema element) => new IpcSchema("array", element: Required(element));
  public static IpcSchema Nullable(IpcSchema element) => new IpcSchema("nullable", element: Required(element));
  public static IpcSchema Optional(IpcSchema element) => new IpcSchema("optional", element: Required(element));
  public static IpcSchema StringLiteral(string value) => StringEnum(value);
  public static IpcSchema StringEnum(params string[] values)
  {
    if (values == null || values.Length == 0) throw new ArgumentException("At least one string enum value is required.", nameof(values));
    var copy = new HashSet<string>(StringComparer.Ordinal);
    foreach (string value in values)
      if (value == null || !copy.Add(value)) throw new ArgumentException("String enum values must be non-null and unique.", nameof(values));
    return new IpcSchema("string enum", values: copy);
  }
  public static IpcSchema Object(params KeyValuePair<string, IpcSchema>[] fields)
  {
    if (fields == null) throw new ArgumentNullException(nameof(fields));
    var copy = new Dictionary<string, IpcSchema>(StringComparer.Ordinal);
    foreach (var field in fields) copy.Add(field.Key, Required(field.Value));
    return new IpcSchema("object", copy);
  }
  private static IpcSchema Required(IpcSchema schema) => schema ?? throw new ArgumentNullException(nameof(schema));
  public static KeyValuePair<string, IpcSchema> Field(string name, IpcSchema schema) => new KeyValuePair<string, IpcSchema>(name, schema);

  internal void Validate(JToken value, string path = "$", int depth = 0)
  {
    if (depth > 32) throw new IpcValidationException(path + ": Maximum data depth exceeded.");
    if (_kind == "optional" || _kind == "nullable")
    {
      if (_kind == "nullable" && value?.Type == JTokenType.Null) return;
      _element.Validate(value, path, depth); return;
    }
    bool numeric = value?.Type == JTokenType.Integer || value?.Type == JTokenType.Float;
    bool valid = _kind == "string" || _values != null ? value?.Type == JTokenType.String :
      _kind == "boolean" ? value?.Type == JTokenType.Boolean :
      _kind == "null" ? value?.Type == JTokenType.Null :
      _kind == "int32" || _kind == "safeint" || _kind == "float64" ? numeric :
      _kind == "array" ? value is JArray : value is JObject;
    if (!valid) throw new IpcValidationException(path + ": Expected " + _kind + ".");
    if (_values != null)
    {
      if (!_values.Contains((string)value)) throw new IpcValidationException(path + ": Unknown string enum value.");
      return;
    }
    if (_kind == "float64")
    {
      if (!double.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
        throw new IpcValidationException(path + ": Expected a finite 64-bit floating point number.");
      return;
    }
    if (_kind == "int32" || _kind == "safeint")
    {
      decimal lower = _kind == "int32" ? int.MinValue : -9007199254740991L;
      decimal upper = _kind == "int32" ? int.MaxValue : 9007199254740991L;
      bool fractional = value.Type == JTokenType.Float &&
        (!double.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) ||
          double.IsNaN(floating) || double.IsInfinity(floating) || Math.Truncate(floating) != floating);
      if (fractional || !decimal.TryParse(value.ToString(Formatting.None), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
        number < lower || number > upper || decimal.Truncate(number) != number)
        throw new IpcValidationException(path + ": Expected " + (_kind == "int32" ? "a signed 32-bit integer." : "an integer in JavaScript's safe integer range."));
      return;
    }
    if (_kind == "array")
    {
      var array = (JArray)value;
      for (int index = 0; index < array.Count; index++) _element.Validate(array[index], path + "[" + index + "]", depth + 1);
      return;
    }
    if (_fields == null) return;
    var obj = (JObject)value;
    foreach (var property in obj.Properties())
      if (!_fields.ContainsKey(property.Name)) throw new IpcValidationException(path + "." + property.Name + ": Unknown field.");
    foreach (var field in _fields)
    {
      if (!obj.TryGetValue(field.Key, StringComparison.Ordinal, out var member))
      {
        if (field.Value._kind == "optional") continue;
        throw new IpcValidationException(path + "." + field.Key + ": A field is required.");
      }
      field.Value.Validate(member, path + "." + field.Key, depth + 1);
    }
  }
}

public sealed class IpcUnit { public static readonly IpcUnit Value = new IpcUnit(); private IpcUnit() { } }

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

internal sealed class IpcContractResolver : DefaultContractResolver
{
  internal IpcContractResolver() { NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false, ProcessDictionaryKeys = false }; }
  protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
  {
    var property = base.CreateProperty(member, memberSerialization);
    if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IpcOptional<>))
    {
      var previous = property.ShouldSerialize;
      var specified = property.PropertyType.GetProperty(nameof(IpcOptional<int>.IsSpecified));
      property.ShouldSerialize = instance => (previous == null || previous(instance)) && (bool)specified.GetValue(property.ValueProvider.GetValue(instance));
    }
    return property;
  }
}

public sealed class IpcCodec<T>
{
  private static readonly DefaultContractResolver Resolver = new IpcContractResolver();
  private readonly IpcSchema _schema;
  public IpcCodec(IpcSchema schema) { _schema = schema ?? throw new ArgumentNullException(nameof(schema)); }
  private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings {
    ContractResolver = Resolver, MissingMemberHandling = MissingMemberHandling.Error,
    TypeNameHandling = TypeNameHandling.None, DateParseHandling = DateParseHandling.None, MaxDepth = 32
  });
  public T Read(string json)
  {
    try
    {
      JToken token = IpcJson.Parse(json); _schema.Validate(token);
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
      _schema.Validate(token); return token.ToString(Formatting.None);
    }
    catch (JsonException error) { throw new IpcValidationException(error.Message); }
  }
}

internal static class IpcJson
{
  internal static JToken Parse(string json)
  {
    if (json == null) throw new IpcValidationException("JSON data is required.");
    using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
    var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    if (reader.Read()) throw new IpcValidationException("Only one JSON value is allowed.");
    return token;
  }
  internal static string Error(string code, string message) => new JObject { ["code"] = code, ["message"] = message }.ToString(Formatting.None);
}
