using System;
using System.Globalization;
using AdofaiIpc.Ports;
namespace AdofaiIpc;

internal static class IpcSchemaValidator {
  internal static void Validate<T>(IpcSchema schema, T value, IValueReader<T> reader, string path = "$", int depth = 0) {
    if (depth > 32) throw new IpcValidationException(path + ": Maximum data depth exceeded.");
    string kind = schema.Kind;
    var actual = reader.Kind(value);
    if (kind == "optional" || kind == "nullable") {
      if (kind == "nullable" && actual == IpcValueKind.Null) return;
      Validate(schema.Element, value, reader, path, depth); return;
    }
    bool numeric = actual == IpcValueKind.Integer || actual == IpcValueKind.Float;
    bool valid = kind == "string" || schema.Values != null ? actual == IpcValueKind.String :
      kind == "boolean" ? actual == IpcValueKind.Boolean :
      kind == "null" ? actual == IpcValueKind.Null :
      kind == "int32" || kind == "safeint" || kind == "float64" ? numeric :
      kind == "array" ? actual == IpcValueKind.Array : actual == IpcValueKind.Object;
    if (!valid) throw new IpcValidationException(path + ": Expected " + kind + ".");
    if (schema.Values != null) {
      if (!schema.Values.Contains(reader.String(value))) throw new IpcValidationException(path + ": Unknown string enum value.");
      return;
    }
    if (kind == "float64") {
      if (!double.TryParse(reader.Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
        throw new IpcValidationException(path + ": Expected a finite 64-bit floating point number.");
      return;
    }
    if (kind == "int32" || kind == "safeint") {
      decimal lower = kind == "int32" ? int.MinValue : -9007199254740991L;
      decimal upper = kind == "int32" ? int.MaxValue : 9007199254740991L;
      bool fractional = actual == IpcValueKind.Float &&
        (!double.TryParse(reader.Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var floating) ||
          double.IsNaN(floating) || double.IsInfinity(floating) || Math.Truncate(floating) != floating);
      if (fractional || !decimal.TryParse(reader.Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
        number < lower || number > upper || decimal.Truncate(number) != number)
        throw new IpcValidationException(path + ": Expected " + (kind == "int32" ? "a signed 32-bit integer." : "an integer in JavaScript's safe integer range."));
      return;
    }
    if (kind == "array") {
      for (int index = 0; index < reader.Count(value); index++) Validate(schema.Element, reader.Item(value, index), reader, path + "[" + index + "]", depth + 1);
      return;
    }
    if (schema.Fields == null) return;
    foreach (string key in reader.Keys(value))
      if (!schema.Fields.ContainsKey(key)) throw new IpcValidationException(path + "." + key + ": Unknown field.");
    foreach (var field in schema.Fields) {
      if (!reader.TryGet(value, field.Key, out var member)) {
        if (field.Value.Kind == "optional") continue;
        throw new IpcValidationException(path + "." + field.Key + ": A field is required.");
      }
      Validate(field.Value, member, reader, path + "." + field.Key, depth + 1);
    }
  }
}
