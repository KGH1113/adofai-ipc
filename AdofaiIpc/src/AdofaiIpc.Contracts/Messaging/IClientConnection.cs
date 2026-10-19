using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

public interface IClientConnection
{
  string PeerId { get; }
  string ConnectionId { get; }
  string Origin { get; }
  void Send(string name, string json, string correlationId = null);
  string SendCommand(string name, string json);
}
