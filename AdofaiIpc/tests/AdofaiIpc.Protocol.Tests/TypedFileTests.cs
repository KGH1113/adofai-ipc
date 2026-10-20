using System.Text;
using AdofaiIpc;
using AdofaiIpc.Contracts;
using AdofaiIpc.Core;
using AdofaiIpc.Runtime;
using Newtonsoft.Json.Linq;
using TypedTest.Generated;

internal static partial class Program
{
  const string FileText = "IPC file · 한글 😀\n";
  static IpcFile TextFile()
  {
    byte[] bytes = Encoding.UTF8.GetBytes(FileText);
    return IpcFile.FromStream(new MemoryStream(bytes), bytes.Length, "example.txt", "text/plain; charset=utf-8");
  }
  static string FileToken(JToken descriptor) => new Uri((string)descriptor["url"]).Segments.Last();
  static string ReadFile(IpcRegistry registry, JToken descriptor)
  {
    string token = FileToken(descriptor);
    Check(!registry.TryTakeDownloadTicket(token, "https://wrong.example", out _, out var denied, out _) && denied == "origin_not_allowed", "file policy granted a different browser origin");
    Check(registry.TryTakeDownloadTicket(token, "http://localhost:5173", out var source, out _, out _), "typed file did not export a usable download");
    using (source)
    using (var output = new MemoryStream())
    {
      source.WriteContent(output);
      Check(!registry.TryTakeDownloadTicket(token, null, out _, out _, out _), "file was readable twice");
      return Encoding.UTF8.GetString(output.ToArray());
    }
  }
  static async Task TestTypedFiles()
  {
    var registry = new IpcRegistry(action => action()) { DownloadBaseUrl = "http://127.0.0.1:32145" };
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), action => action(), () => false);
    using var mod = CreateTypedFeature(runtime);
    using var peer = new TypedPeer(registry, "files", "http://localhost:5173");
    peer.Subscribe(); await peer.Register();
    var response = await peer.Call("rpc.mod.downloadtext", new { });
    Check(response.Name == "rpc.result" && (string)response.Payload["fileName"] == "example.txt" && (long)response.Payload["byteLength"] == Encoding.UTF8.GetByteCount(FileText), "typed file metadata changed");
    Check(ReadFile(registry, response.Payload) == FileText, "typed file bytes changed");
    var bundle = await peer.Call("rpc.mod.downloadbundle", new { });
    Check(bundle.Payload["optional"].Type == JTokenType.Null && ReadFile(registry, bundle.Payload["content"]) == FileText, "nullable file/model serialization failed");
    Check(ReadFile(registry, bundle.Payload["files"][0]) == "", "empty file array member did not transfer");

    Check(mod.TryGetApp(peer.AppId, out var app), "file target app was unavailable");
    JToken pushed = null;
    peer.OnCommand = message => { pushed = message.Payload["args"]["file"]; peer.Reply(message, (long)pushed["byteLength"]); };
    Check(await app.ReceiveFileAsync(new ReceiveFileRequest { File = TextFile() }) == Encoding.UTF8.GetByteCount(FileText), "mod-initiated file callback failed");
    Check(ReadFile(registry, pushed) == FileText, "callback acknowledgement revoked an unread file");

    var tracked = new FileTrackingStream(new byte[] { 1, 2, 3 });
    var single = IpcFile.FromStream(tracked, 3, "once.bin");
    await app.ReceiveFileAsync(new ReceiveFileRequest { File = single });
    bool reused = false;
    try { await app.ReceiveFileAsync(new ReceiveFileRequest { File = single }); } catch (IpcValidationException) { reused = true; }
    Check(reused, "the same C# file source was exported twice");
    Check(registry.TryTakeDownloadTicket(FileToken(pushed), null, out var active, out _, out _), "callback source was missing");
    Check(!tracked.WasDisposed, "file stream closed before consumption");

    var failed = new FileTrackingStream(new byte[] { 4 });
    peer.OnCommand = message => { pushed = message.Payload["args"]["file"]; peer.Reply(message, "bad result"); };
    try { await app.ReceiveFileAsync(new ReceiveFileRequest { File = IpcFile.FromStream(failed, 1, "failed.bin") }); Check(false, "invalid callback reply succeeded"); }
    catch (IpcValidationException) { }
    Check(failed.WasDisposed && !registry.TryTakeDownloadTicket(FileToken(pushed), null, out _, out _, out _), "failed callback retained a file ticket");

    var canceled = new FileTrackingStream(new byte[] { 5 });
    var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    peer.OnCommand = message => { pushed = message.Payload["args"]["file"]; delivered.TrySetResult(true); };
    using (var cancel = new CancellationTokenSource())
    {
      var pending = app.ReceiveFileAsync(new ReceiveFileRequest { File = IpcFile.FromStream(canceled, 1, "canceled.bin") }, cancel.Token);
      await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
      try { await pending; Check(false, "file callback cancellation was ignored"); } catch (OperationCanceledException) { }
      Check(canceled.WasDisposed && !registry.TryTakeDownloadTicket(FileToken(pushed), null, out _, out _, out _), "canceled callback retained a file ticket");
    }

    peer.OnCommand = message => { pushed = message.Payload["args"]["file"]; peer.Reply(message, 0); };
    var pendingStream = new FileTrackingStream(new byte[] { 6 });
    await app.ReceiveFileAsync(new ReceiveFileRequest { File = IpcFile.FromStream(pendingStream, 1, "pending.bin") });
    peer.Dispose();
    Check(tracked.WasDisposed && pendingStream.WasDisposed, "session disconnect leaked an active or unread file stream");
    Check(!registry.TryTakeDownloadTicket(FileToken(pushed), null, out _, out _, out _), "session disconnect left a file downloadable");
    active.Dispose();

    var unused = new FileTrackingStream(Array.Empty<byte>());
    IpcFile.FromStream(unused, 0, "unused.bin").Dispose();
    Check(unused.WasDisposed, "disposing an unsent file did not close its source");
    string path = Path.GetTempFileName();
    try { File.WriteAllText(path, FileText); using var file = IpcFile.FromPath(path, "text/plain"); Check(file.Size == new FileInfo(path).Length && file.Name == Path.GetFileName(path), "path source metadata failed"); }
    finally { File.Delete(path); }
    TestFileWriters(runtime, registry);
    runtime.Shutdown();
    await TestTypedFileFailures();
    Console.WriteLine("PASS typed files: results, nested/optional/empty files, targeted callbacks, one-use sources, cancellation, disconnect and writer bounds.");
  }
  static void TestFileWriters(IIpcRuntime runtime, IpcRegistry registry)
  {
    using var feature = runtime.RegisterFeature(new FeatureDescription("file-writers", "2.0.0", allowedOrigins: new[] { "https://allowed.example" }), _ => { });
    var files = (IFeatureDownloads)feature;
    int writes = 0;
    using (var ticket = files.ExportFile(output => { writes++; output.WriteByte(7); }, 1, "writer.bin", "application/octet-stream", default, default))
    {
      Check(writes == 0, "file writer ran during message serialization");
      Check(registry.TryTakeDownloadTicket(new Uri(ticket.Url).Segments.Last(), null, out var source, out _, out _), "writer source missing");
      using (source) using (var destination = new MemoryStream()) { source.WriteContent(destination); Check(writes == 1 && destination.ToArray().SequenceEqual(new byte[] { 7 }), "lazy writer content failed"); }
    }
    foreach (int written in new[] { 0, 2 })
    {
      using var ticket = files.ExportFile(output => { for (int i = 0; i < written; i++) output.WriteByte(1); }, 1, "bad.bin", "application/octet-stream", default, default);
      Check(registry.TryTakeDownloadTicket(new Uri(ticket.Url).Segments.Last(), null, out var source, out _, out _), "invalid writer source missing");
      using (source) using (var destination = new MemoryStream())
      {
        bool rejected = false;
        try { source.WriteContent(destination); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "writer ignored its declared length");
      }
    }
    var revoked = new FileTrackingStream(new byte[] { 8 });
    using var lease = files.ExportFile(revoked, 1, "revoked.bin", "application/octet-stream", default, default);
    feature.Dispose();
    Check(revoked.WasDisposed && !registry.TryTakeDownloadTicket(new Uri(lease.Url).Segments.Last(), null, out _, out _, out _), "mod unregister leaked a file source");
    using var strict = runtime.RegisterFeature(new FeatureDescription("strict-files", "2.0.0", allowedOrigins: new[] { "http://localhost" }) { ExactOrigins = true }, _ => { });
    using var strictLease = ((IFeatureDownloads)strict).ExportFile(output => { }, 0, "empty", "application/octet-stream", default, default);
    string strictToken = new Uri(strictLease.Url).Segments.Last();
    Check(!registry.TryTakeDownloadTicket(strictToken, "http://localhost:5173", out _, out _, out _), "typed download changed an exact default-port policy into a port wildcard");
    Check(registry.TryTakeDownloadTicket(strictToken, "http://localhost", out var strictSource, out _, out _), "typed download rejected the exact allowed origin");
    strictSource.Dispose();
  }

  static async Task TestTypedFileFailures()
  {
    var registry = new IpcRegistry(action => action()) { DownloadBaseUrl = "http://127.0.0.1:32145" };
    var runtime = new RoutedRuntime(registry, new RuntimeInfo("2.0.0", 1, 3), action => action(), () => false);
    var owned = new List<FileTrackingStream>();
    IpcFile Next()
    {
      var stream = new FileTrackingStream(new byte[] { 1 }); owned.Add(stream);
      return IpcFile.FromStream(stream, 1, "one.bin");
    }
    Func<FileBundle> bundle = () => new FileBundle { Content = Next(), Files = null };
    using (var mod = CreateTypedFeature(runtime, createBundle: () => bundle()))
    using (var peer = new TypedPeer(registry, "file-failures", "http://localhost:5173"))
    {
      peer.Subscribe(); await peer.Register();
      var invalid = await peer.Call("rpc.mod.downloadbundle", new { });
      Check((string)invalid.Payload["code"] == "invalid_result" && owned.All(stream => stream.WasDisposed), "invalid result leaked a file prepared before validation");
      owned.Clear();
      bundle = () => new FileBundle { Content = Next(), Files = Enumerable.Range(0, 129).Select(_ => Next()).ToArray() };
      var tooMany = await peer.Call("rpc.mod.downloadbundle", new { });
      Check((string)tooMany.Payload["code"] == "download_ticket_limit" && owned.Count == 130 && owned.All(stream => stream.WasDisposed), "partial file export did not roll back every source, including the unexported tail");
      owned.Clear();
      bundle = () => { var file = Next(); return new FileBundle { Content = file, Files = new[] { file, Next() } }; };
      var duplicate = await peer.Call("rpc.mod.downloadbundle", new { });
      Check((string)duplicate.Payload["code"] == "invalid_result" && owned.All(stream => stream.WasDisposed), "duplicate file use leaked an unexported sibling");
      bundle = () => new FileBundle { Content = TextFile(), Files = Array.Empty<IpcFile>() };
      var valid = await peer.Call("rpc.mod.downloadbundle", new { });
      Check(valid.Name == "rpc.result" && ((JObject)valid.Payload).Property("optional") == null && ReadFile(registry, valid.Payload["content"]) == FileText, "failed file exports exhausted the runtime quota or lost omission");
      bundle = () => new FileBundle { Content = TextFile(), Files = Array.Empty<IpcFile>(), Optional = TextFile() };
      var optional = await peer.Call("rpc.mod.downloadbundle", new { });
      Check(ReadFile(registry, optional.Payload["optional"]) == FileText && ReadFile(registry, optional.Payload["content"]) == FileText, "specified optional file failed to transfer");
    }
    using (var legacyMod = CreateTypedFeature(new FilelessRuntime(runtime)))
    using (var peer = new TypedPeer(registry, "fileless-runtime", "http://localhost:5173"))
    {
      peer.Subscribe(); await peer.Register();
      Check((await peer.Call("rpc.mod.noop", new { })).Name == "rpc.result", "optional file support became required for ordinary calls");
      var unavailable = await peer.Call("rpc.mod.downloadtext", new { });
      Check((string)unavailable.Payload["code"] == "file_transfer_unavailable", "an older runtime did not fail with an actionable file error");
    }
    runtime.Shutdown();
  }

  sealed class FilelessRuntime : IIpcRuntime
  {
    private readonly IIpcRuntime _runtime;
    public FilelessRuntime(IIpcRuntime runtime) { _runtime = runtime; }
    public RuntimeInfo Info => _runtime.Info;
    public bool IsGameThread => _runtime.IsGameThread;
    public void RunOnGameThread(Action action) => _runtime.RunOnGameThread(action);
    public ILocalPeer CreateLocalPeer(string name) => _runtime.CreateLocalPeer(name);
    public IFeatureRegistration RegisterFeature(FeatureDescription description, Action<IFeatureBuilder> configure)
      => new FilelessFeature(_runtime.RegisterFeature(description, configure));
  }
  sealed class FilelessFeature : IFeatureRegistration
  {
    private readonly IFeatureRegistration _feature;
    public FilelessFeature(IFeatureRegistration feature) { _feature = feature; }
    public string Name => _feature.Name;
    public void MarkInitializing() => _feature.MarkInitializing();
    public void MarkReady() => _feature.MarkReady();
    public void MarkError(string code, string message) => _feature.MarkError(code, message);
    public void Publish(string name, string json) => _feature.Publish(name, json);
    public void SendToPeer(string peerId, string name, string json, string correlationId = null) => _feature.SendToPeer(peerId, name, json, correlationId);
    public string SendCommandToPeer(string peerId, string name, string json) => _feature.SendCommandToPeer(peerId, name, json);
    public void Dispose() => _feature.Dispose();
  }
  sealed class FileTrackingStream : MemoryStream
  {
    public bool WasDisposed;
    public FileTrackingStream(byte[] bytes) : base(bytes) { }
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
  }
}
