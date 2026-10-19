using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Contracts;

/// <summary>Optional runtime extension for a captured connection's live transport lifetime.</summary>
public interface IClientConnectionState
{
  bool IsConnected { get; }
  CancellationToken CancellationToken { get; }
}
