using System.Net;
using System.Text;
using AdofaiIpc;
using AdofaiIpc.Core;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
  const string AllowedOrigin = "https://allowed.example";
  static readonly byte[] LargeFileBytes = CreateLargeFileBytes();

  static byte[] CreateLargeFileBytes()
  {
    var bytes = new byte[3 * 1024 * 1024 + 123];
    new Random(7).NextBytes(bytes);
    return bytes;
  }

  static void RegisterOpenedFileCommands(AdofaiIpcNamespace ns)
  {
    ns.RegisterDownloadCommand("large.download", cmd => new IpcDownloadSource(new MemoryStream(LargeFileBytes), LargeFileBytes.Length, "large.bin", "video/mp4"));
    ns.RegisterDownloadCommand("writer.download", cmd => new IpcDownloadSource(output => output.Write(new byte[] { 1, 2, 3 }, 0, 3), 3, "writer.bin"));
  }

  static void TestByteRanges()
  {
    static string Describe(string header, long length)
    {
      var range = ByteRangeRequest.Parse(header, length);
      return range.Kind + ":" + range.Start + "-" + range.End;
    }
    Check(Describe(null, 12) == "Full:0-12", "missing range");
    Check(Describe("bytes=2-6", 12) == "Partial:2-7", "closed range");
    Check(Describe("bytes=8-", 12) == "Partial:8-12", "open range");
    Check(Describe("bytes=-4", 12) == "Partial:8-12", "suffix range");
    Check(Describe("bytes=-40", 12) == "Partial:0-12", "suffix longer than the file");
    Check(Describe("bytes=4-400", 12) == "Partial:4-12", "range past the end");
    Check(Describe("BYTES=0-0", 12) == "Partial:0-1", "unit is case-insensitive");
    Check(Describe("bytes=12-", 12) == "Unsatisfiable:0-0", "range after the end");
    Check(Describe("bytes=-0", 12) == "Unsatisfiable:0-0", "empty suffix");
    Check(Describe("bytes=0-", 0) == "Unsatisfiable:0-0", "range of an empty file");
    Check(Describe("bytes=6-2", 12) == "Full:0-12", "reversed range is ignored");
    Check(Describe("bytes=0-1,4-5", 12) == "Full:0-12", "several ranges are ignored");
    Check(Describe("items=0-1", 12) == "Full:0-12", "other units are ignored");
    Check(Describe("bytes=a-b", 12) == "Full:0-12", "malformed range is ignored");
    Console.WriteLine("PASS byte ranges: single, open, suffix, clamped, unsatisfiable and ignored ranges.");
  }

  static async Task<string> RequestDownloadUrl(Socket client, string command, string id)
  {
    await client.Send("command", "test-mod", command, id);
    await client.Next("command.accepted");
    return (string)(await client.Next("download.ready")).Payload["url"];
  }

  static async Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string url, string origin = null, string range = null)
  {
    var request = new HttpRequestMessage(method, url);
    // Browsers and fetch send Content-Length: 0 with an empty POST; the server requires it.
    if (method == HttpMethod.Post) request.Content = new ByteArrayContent(Array.Empty<byte>());
    if (origin != null) request.Headers.Add("Origin", origin);
    if (range != null) request.Headers.TryAddWithoutValidation("Range", range);
    return await http.SendAsync(request);
  }

  static async Task<string> OpenFile(HttpClient http, string downloadUrl)
  {
    using var opened = await Send(http, HttpMethod.Post, downloadUrl.Replace("/ipc/download/", "/ipc/files/open/"), AllowedOrigin);
    Check(opened.StatusCode == HttpStatusCode.OK, "opening a seekable file failed: " + opened.StatusCode);
    Check(opened.Headers.GetValues("Access-Control-Allow-Origin").Single() == AllowedOrigin, "open response CORS");
    return (string)JObject.Parse(await opened.Content.ReadAsStringAsync())["url"];
  }

  static async Task TestOpenedFilesOverHttp(Socket client, HttpClient http, IpcRegistry registry)
  {
    string downloadUrl = await RequestDownloadUrl(client, "level.download", "open-level");
    string url = await OpenFile(http, downloadUrl);
    Check(new Uri(url).AbsolutePath.StartsWith("/ipc/files/") && new Uri(url).Host == "127.0.0.1", "opened file URL");
    Check((await Send(http, HttpMethod.Get, downloadUrl)).StatusCode == HttpStatusCode.NotFound, "opening left the download ticket readable");

    using (var whole = await Send(http, HttpMethod.Get, url))
    {
      Check(whole.StatusCode == HttpStatusCode.OK && await whole.Content.ReadAsStringAsync() == "a level file", "whole opened file");
      Check(whole.Headers.AcceptRanges.Contains("bytes") && whole.Content.Headers.ContentLength == 12, "whole file range support and length");
      Check(whole.Content.Headers.ContentDisposition?.DispositionType == "inline", "opened files are shown inline");
      Check(whole.Headers.GetValues("Content-Security-Policy").Single() == "sandbox", "opened documents are sandboxed");
      Check(!whole.Headers.Contains("Access-Control-Allow-Origin"), "a request without Origin got a CORS grant");
    }

    async Task<string> Part(string range, string expected, string contentRange)
    {
      using var response = await Send(http, HttpMethod.Get, url, AllowedOrigin, range);
      string body = await response.Content.ReadAsStringAsync();
      Check(response.StatusCode == HttpStatusCode.PartialContent && body == expected, "range " + range + " returned " + response.StatusCode + " '" + body + "'");
      Check(response.Content.Headers.ContentRange?.ToString() == contentRange && response.Content.Headers.ContentLength == expected.Length, "range " + range + " headers");
      Check(response.Headers.GetValues("Access-Control-Allow-Origin").Single() == AllowedOrigin, "range CORS");
      return body;
    }
    await Part("bytes=2-6", "level", "bytes 2-6/12");
    await Part("bytes=-4", "file", "bytes 8-11/12");
    await Part("bytes=8-", "file", "bytes 8-11/12");
    await Part("bytes=0-0", "a", "bytes 0-0/12");
    using (var after = await Send(http, HttpMethod.Get, url, null, "bytes=12-"))
      Check(after.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && after.Content.Headers.ContentRange?.ToString() == "bytes */12", "range after the end");
    Check((await Send(http, HttpMethod.Get, url, "https://forbidden.example")).StatusCode == HttpStatusCode.Forbidden, "opened file ignored its origin policy");

    using (var preflight = await Send(http, HttpMethod.Options, url, AllowedOrigin))
      Check(preflight.StatusCode == HttpStatusCode.NoContent && preflight.Headers.GetValues("Access-Control-Allow-Headers").Single() == "Range", "range preflight");

    using (var closed = await Send(http, HttpMethod.Post, url + "/close", AllowedOrigin))
      Check(closed.StatusCode == HttpStatusCode.NoContent, "closing an opened file");
    Check((await Send(http, HttpMethod.Get, url)).StatusCode == HttpStatusCode.NotFound, "closed file stayed readable");

    // A source that cannot seek keeps its ticket, so it can still be read once.
    string writerUrl = await RequestDownloadUrl(client, "writer.download", "open-writer");
    using (var refused = await Send(http, HttpMethod.Post, writerUrl.Replace("/ipc/download/", "/ipc/files/open/"), AllowedOrigin))
    {
      Check(refused.StatusCode == HttpStatusCode.Conflict && (string)JObject.Parse(await refused.Content.ReadAsStringAsync())["code"] == "file_not_seekable", "unseekable source was opened");
      Check(refused.Headers.GetValues("Access-Control-Allow-Origin").Single() == AllowedOrigin, "refusal CORS lets the app read its code");
    }
    using (var once = await Send(http, HttpMethod.Get, writerUrl))
      Check(once.StatusCode == HttpStatusCode.OK && (await once.Content.ReadAsByteArrayAsync()).SequenceEqual(new byte[] { 1, 2, 3 }), "refused open consumed the ticket");
    using (var missing = await Send(http, HttpMethod.Post, writerUrl.Replace("/ipc/download/", "/ipc/files/open/"), AllowedOrigin))
      Check(missing.StatusCode == HttpStatusCode.NotFound && (string)JObject.Parse(await missing.Content.ReadAsStringAsync())["code"] == "file_expired", "opening a used ticket");

    // Large ranges stream with an exact length instead of being buffered first.
    string large = await OpenFile(http, await RequestDownloadUrl(client, "large.download", "open-large"));
    using (var all = await Send(http, HttpMethod.Get, large))
      Check(all.Content.Headers.ContentLength == LargeFileBytes.Length && (await all.Content.ReadAsByteArrayAsync()).SequenceEqual(LargeFileBytes), "large opened file");
    using (var middle = await Send(http, HttpMethod.Get, large, null, "bytes=1000000-2999999"))
    {
      byte[] bytes = await middle.Content.ReadAsByteArrayAsync();
      Check(middle.StatusCode == HttpStatusCode.PartialContent && bytes.SequenceEqual(LargeFileBytes.Skip(1000000).Take(2000000)), "large range");
    }
    var reads = Enumerable.Range(0, 4).Select(async index =>
    {
      using var response = await Send(http, HttpMethod.Get, large, null, "bytes=" + (index * 500000) + "-" + (index * 500000 + 499999));
      return (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(LargeFileBytes.Skip(index * 500000).Take(500000));
    }).ToArray();
    Check((await Task.WhenAll(reads)).All(matches => matches), "concurrent ranges of one file");

    // Unused files close after the idle lifetime.
    registry.PruneIdleFiles(DateTime.UtcNow + IpcOpenedFileStore.IdleLifetime + TimeSpan.FromSeconds(1));
    Check((await Send(http, HttpMethod.Get, large)).StatusCode == HttpStatusCode.NotFound, "idle opened file stayed readable");
    Console.WriteLine("PASS opened files: open, inline whole and ranged reads, origins, preflight, close, unseekable refusal, large and concurrent ranges, idle expiry.");
  }
}
