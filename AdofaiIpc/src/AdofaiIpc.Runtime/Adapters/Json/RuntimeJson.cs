using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AdofaiIpc.Runtime;

internal static class RuntimeJson
{
  internal static JToken Parse(string json)
  {
    if (json == null) return new JObject();
    if (System.Text.Encoding.UTF8.GetByteCount(json) > IpcProtocol.MaxMessageBytes)
      throw new IpcMessageTooLargeException(System.Text.Encoding.UTF8.GetByteCount(json));
    using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
    JToken value = JToken.Load(reader);
    if (reader.Read()) throw new JsonReaderException("Only one JSON value is allowed.");
    return value;
  }
  internal static string Json(JToken token) => (token ?? new JObject()).ToString(Formatting.None);
}
