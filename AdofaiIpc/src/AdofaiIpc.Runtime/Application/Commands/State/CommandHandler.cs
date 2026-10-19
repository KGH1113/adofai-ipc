using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdofaiIpc.Core;

internal sealed class CommandHandler
{ internal readonly Func<IpcCommand, Task> Action; internal readonly bool MainThread; internal CommandHandler(Func<IpcCommand, Task> action, bool mainThread) { Action = action; MainThread = mainThread; } }
