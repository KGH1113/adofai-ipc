using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public sealed class LocalMessage
{
  public string Kind { get; }
  public string Namespace { get; }
  public string Name { get; }
  public string Id { get; }
  public string CorrelationId { get; }
  public string Json { get; }
  public LocalMessage(string kind, string ns, string name, string id, string correlationId, string json)
  { Kind = kind; Namespace = ns; Name = name; Id = id; CorrelationId = correlationId; Json = json; }
}
