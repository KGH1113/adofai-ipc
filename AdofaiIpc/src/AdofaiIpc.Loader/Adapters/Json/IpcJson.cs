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
