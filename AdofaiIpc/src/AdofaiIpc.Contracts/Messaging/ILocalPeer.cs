using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public interface ILocalPeer : IDisposable
{
  bool IsClosed { get; }
  event Action<LocalMessage> Message;
  void Subscribe(string feature);
  void Unsubscribe(string feature);
  string SendCommand(string feature, string name, string json);
  void SendEvent(string feature, string name, string json, string correlationId = null);
}
