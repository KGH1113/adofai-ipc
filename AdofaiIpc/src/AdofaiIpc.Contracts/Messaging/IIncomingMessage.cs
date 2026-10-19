using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public interface IIncomingMessage
{
  string Id { get; }
  string Name { get; }
  string Json { get; }
  IClientConnection Client { get; }
  CancellationToken CancellationToken { get; }
  void Reply(string name, string json);
  void Reject(string code, string message);
  void Reject(string code, string message, string detailsJson);
  // Ownership of the source transfers to the runtime, including failure and cancellation paths.
  void ReplyDownload(string name, Stream source, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null);
  void ReplyDownload(string name, Action<Stream> writer, long byteLength, string fileName,
    string contentType = "application/octet-stream", string metadataJson = null);
}
