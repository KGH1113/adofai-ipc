using System;
using System.Collections.Generic;
namespace AdofaiIpc;

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

  internal string Kind => _kind;
  internal Dictionary<string, IpcSchema> Fields => _fields;
  internal IpcSchema Element => _element;
  internal HashSet<string> Values => _values;
}
