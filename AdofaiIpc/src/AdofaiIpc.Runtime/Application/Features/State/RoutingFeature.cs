using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Core;

internal sealed class RoutingFeature
{
  internal readonly AdofaiIpcNamespace Handle;
  internal readonly Dictionary<string, CommandHandler> Commands = new Dictionary<string, CommandHandler>();
  internal IpcNamespaceStatus Status = IpcNamespaceStatus.Initializing;
  internal string ErrorCode, ErrorMessage;
  internal RoutingFeature(AdofaiIpcNamespace handle) { Handle = handle; }
}
