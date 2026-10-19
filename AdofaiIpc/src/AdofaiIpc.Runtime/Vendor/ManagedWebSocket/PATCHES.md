# Local transport changes

The embedded transport retains its upstream license and isolated namespace.

v2 replaces asynchronous delegate BeginInvoke/EndInvoke calls with ThreadPool callbacks.
Modern .NET does not implement asynchronous delegate invocation; this prevented real WebSocket
tests from receiving messages or closing connections. ThreadPool is also available in Unity/Mono.
WebSocket asynchronous operation errors are logged through the existing error event.
Pending reads completed during a normal Closing/Closed transition signal receive completion
without reporting a fatal stream-disposal error. Errors while Open still use the upstream path.
