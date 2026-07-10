using System;
using System.Threading;

namespace AdofaiIpc.Unity;

public static class MainThreadInvoker
{
  private const int TimeoutMilliseconds = 10000;

  public static object Invoke(Func<object> action)
  {
    if (MainThreadDispatcher.IsMainThread) return action();

    object result = null;
    Exception exception = null;

    using ManualResetEventSlim complete = new ManualResetEventSlim(false);

    MainThreadDispatcher.Run(() =>
    {
      try
      {
        result = action();
      }
      catch (Exception e)
      {
        exception = e;
      }
      finally
      {
        complete.Set();
      }
    });

    if (!complete.Wait(TimeoutMilliseconds))
    {
      throw new TimeoutException("Main thread IPC handler timed out.");
    }

    if (exception != null) throw exception;
    return result;
  }
}
