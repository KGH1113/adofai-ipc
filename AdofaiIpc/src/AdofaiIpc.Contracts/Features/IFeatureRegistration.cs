using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public interface IFeatureRegistration : IDisposable
{
  string Name { get; }
  void MarkInitializing();
  void MarkReady();
  void MarkError(string code, string message);
  void Publish(string name, string json);
  void SendToPeer(string peerId, string name, string json, string correlationId = null);
  string SendCommandToPeer(string peerId, string name, string json);
}
