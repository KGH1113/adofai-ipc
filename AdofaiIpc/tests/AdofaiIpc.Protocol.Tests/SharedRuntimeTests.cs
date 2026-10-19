using System.Collections.Concurrent;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using AdofaiIpc.Runtime;
using AdofaiIpc.Server;

internal static partial class Program
{
  static async Task TestSharedRuntime()
  {
    var queue = new ConcurrentQueue<Action>();
    var registry = new IpcRegistry(queue.Enqueue);
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), queue.Enqueue, () => true);
    var server = new IpcServer(registry); server.Start();
    try
    {
      int firstExecutions = 0;
      IFeatureBuilder retainedBuilder = null;
      var first = runtime.RegisterFeature(new FeatureDescription("first-mod", "1.0.0"), builder => {
        retainedBuilder = builder;
        builder.OnCommand("work", IpcExecutionContext.GameThread, message => {
          firstExecutions++; message.Reply("worked", "{}"); return Task.CompletedTask;
        });
      });
      first.MarkReady();
      var second = runtime.RegisterFeature(new FeatureDescription("second-mod", "1.0.0", 2), builder => {
        builder.OnCommand("echo", IpcExecutionContext.Background, message => {
          message.Reply("echoed", message.Json); return Task.CompletedTask;
        });
        builder.OnClientAttached(client => client.Send("state.snapshot", "{\"value\":7}"));
        builder.OnCommand("download", IpcExecutionContext.Background, message => {
          message.ReplyDownload("file.ready", new MemoryStream(new byte[] { 1, 2, 3 }), 3, "test.bin");
          return Task.CompletedTask;
        });
      });
      second.MarkReady();
      CheckInvalidOperation(() => runtime.RegisterFeature(new FeatureDescription("second-mod", "9.0.0"), _ => { }), "duplicate registration stole another feature");
      CheckInvalidOperation(() => retainedBuilder.OnCommand("late", IpcExecutionContext.Background, _ => Task.CompletedTask), "retained builder accepted late configuration");
      CheckInvalidOperation(() => runtime.RegisterFeature(new FeatureDescription("failed-mod", "1.0.0"), _ => throw new InvalidOperationException()), "configuration error hidden");
      using (runtime.RegisterFeature(new FeatureDescription("failed-mod", "1.0.0"), _ => { })) { }

      using var client = await Socket.Connect(server.Port, "https://allowed.example");
      await client.Next("connection.welcome");
      await client.Send("subscribe", "first-mod", "namespace.subscribe", "first-sub");
      await client.Next("namespace.subscribed"); await client.Next("namespace.changed");
      await client.Send("subscribe", "second-mod", "namespace.subscribe", "second-sub");
      await client.Next("namespace.subscribed");
      Check((int)(await client.Next("namespace.changed")).Payload["featureProtocolMajor"] == 2, "feature protocol version missing");
      while (queue.TryDequeue(out var action)) action();
      await client.Next("state.snapshot");
      await client.Send("command", "first-mod", "work", "pending"); await client.Next("command.accepted");
      first.Dispose(); await client.Next("namespace.changed");
      while (queue.TryDequeue(out var action)) action();
      Check(firstExecutions == 0, "disabled feature executed queued command");
      Check(server.Port != 0, "registration disposal stopped process server");
      await client.Send("command", "second-mod", "echo", "survives", new { value = 42 }); await client.Next("command.accepted");
      Check((await client.Next("echoed")).CorrelationId == "survives", "remaining feature lost socket");
      using (var replacement = runtime.RegisterFeature(new FeatureDescription("first-mod", "1.1.0"), _ => { }))
      {
        replacement.MarkReady(); first.Dispose();
        Check(registry.GetNamespaceStatus("first-mod") == global::AdofaiIpc.IpcNamespaceStatus.Ready, "old registration removed replacement");
      }
      using var local = runtime.CreateLocalPeer("consumer");
      var completion = new TaskCompletionSource<LocalMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
      local.Message += message => { if (message.Name == "echoed") completion.TrySetResult(message); };
      local.Subscribe("second-mod");
      string id = local.SendCommand("second-mod", "echo", "{\"value\":9}");
      Check((await completion.Task.WaitAsync(TimeSpan.FromSeconds(3))).CorrelationId == id, "contract local peer did not route");
      await client.Send("command", "second-mod", "download", "ticket"); await client.Next("command.accepted");
      var ticket = await client.Next("file.ready");
      second.Dispose();
      using var http = new HttpClient();
      Check((await http.GetAsync((string)ticket.Payload["url"])).StatusCode == System.Net.HttpStatusCode.NotFound, "feature disposal retained download ticket");
      Check((await http.GetAsync(server.Url + "ipc/health")).IsSuccessStatusCode, "zero features stopped server");
      runtime.Shutdown(); server.Stop();
      Check(local.IsClosed, "host shutdown retained local peer");
      CheckInvalidOperation(() => runtime.RegisterFeature(new FeatureDescription("after-stop", "1.0.0"), _ => { }), "stopped host accepted registration");
      Console.WriteLine("PASS v2 shared runtime: independent lifetimes, duplicate rejection, configuration rollback, stale handles, snapshots, downloads and local peers.");
    }
    finally { runtime.Shutdown(); server.Stop(); }
  }
}
