using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdofaiIpc;
using AdofaiIpc.Core;

byte[] payload = Enumerable.Range(0, 200_000).Select(index => (byte)(index % 251)).ToArray();
using (var source = new IpcDownloadSource(new MemoryStream(payload), payload.Length, "sample.wav", "audio/wav"))
using (var destination = new MemoryStream())
{
  source.WriteContent(destination);
  Assert(payload.SequenceEqual(destination.ToArray()), "streamed bytes differ from source");
}
Console.WriteLine("PASS stream larger than copy buffer");

using (var source = new IpcDownloadSource(new MemoryStream(new byte[3]), 4, "sample.wav"))
{
  AssertThrows<EndOfStreamException>(() => source.WriteContent(new MemoryStream()));
}
using (var source = new CallbackSource(stream => stream.Write(new byte[5], 0, 5), 4))
{
  AssertThrows<InvalidDataException>(() => source.WriteContent(new MemoryStream()));
}
Console.WriteLine("PASS declared length enforcement");

var tickets = new IpcDownloadTicketStore();
var ticketSource = new IpcDownloadSource(new MemoryStream(payload), payload.Length, "sample.wav");
Assert(tickets.TryCreate("test", ticketSource, new[] { "https://example.test" }, out string token, out _, out _), "ticket creation failed");
Assert(!tickets.TryTake(token, "https://other.test", out _, out string denied, out _), "unapproved origin consumed ticket");
Assert(denied == IpcErrorCodes.OriginNotAllowed, "wrong origin error");
Assert(tickets.TryTake(token, "https://example.test", out IpcDownloadSource taken, out _, out _), "approved origin did not consume ticket");
Assert(ReferenceEquals(ticketSource, taken), "ticket returned wrong source");
Assert(!tickets.TryTake(token, "https://example.test", out _, out _, out _), "ticket was reusable");
taken.Dispose();
Console.WriteLine("PASS origin check and single-use ticket");

var corsTickets = new IpcDownloadTicketStore();
Assert(corsTickets.TryCreate("test", new CallbackSource(_ => { }, 0), new[] { "https://allowed.test" }, out string corsToken, out _, out _), "CORS ticket creation failed");
Assert(!corsTickets.TryTake(corsToken, "https://forbidden.test", out _, out _, out _, out _), "forbidden Origin consumed CORS ticket");
Assert(corsTickets.TryTake(corsToken, "https://allowed.test", out var corsSource, out string approvedOrigin, out _, out _), "allowed CORS ticket rejected");
Assert(approvedOrigin == "https://allowed.test", "CORS grant must come from an explicitly authorized ticket");
corsSource.Dispose();
Assert(corsTickets.TryCreate("test", new CallbackSource(_ => { }, 0), null, out string unrestrictedToken, out _, out _), "unrestricted ticket creation failed");
Assert(corsTickets.TryTake(unrestrictedToken, "https://arbitrary.test", out var unrestrictedSource, out string unrestrictedGrant, out _, out _), "native/unrestricted ticket failed");
Assert(unrestrictedGrant == null, "unrestricted ticket must never reflect a CORS Origin");
unrestrictedSource.Dispose();
Console.WriteLine("PASS explicit allowlist CORS grant and unrestricted no-grant");

var concurrentTickets = new IpcDownloadTicketStore();
Assert(concurrentTickets.TryCreate("test", new CallbackSource(_ => { }, 0), null, out string concurrentToken, out _, out _), "concurrent ticket creation failed");
int winners = 0;
Parallel.For(0, 32, index =>
{
  if (concurrentTickets.TryTake(concurrentToken, null, out IpcDownloadSource winner, out _, out _))
  {
    Interlocked.Increment(ref winners);
    winner.Dispose();
  }
});
Assert(winners == 1, "concurrent requests consumed a ticket more than once");
Console.WriteLine("PASS atomic concurrent consumption");

var revokedTickets = new IpcDownloadTicketStore();
Assert(revokedTickets.TryCreate("test", new CallbackSource(_ => { }, 0), null, out string revokedToken, out _, out _), "revocable ticket creation failed");
revokedTickets.RevokeNamespace("test");
Assert(!revokedTickets.TryTake(revokedToken, null, out _, out _, out _), "namespace revocation left a valid ticket");
Console.WriteLine("PASS namespace revocation");

var expiringTickets = new IpcDownloadTicketStore();
var expiringStream = new MemoryStream(new byte[1]);
Assert(expiringTickets.TryCreate("test", new IpcDownloadSource(expiringStream, 1, "sample.wav"), null, out string expiringToken, out _, out _), "expiring ticket creation failed");
expiringTickets.PruneExpired(DateTime.UtcNow.AddMinutes(2));
Assert(!expiringTickets.TryTake(expiringToken, null, out _, out _, out _), "expired ticket remained valid");
AssertThrows<ObjectDisposedException>(() => expiringStream.ReadByte());
Console.WriteLine("PASS expired ticket disposes its source");

var boundedTickets = new IpcDownloadTicketStore();
for (int index = 0; index < IpcDownloadTicketStore.MaxTicketCount; index++)
{
  Assert(boundedTickets.TryCreate("test", new CallbackSource(_ => { }, 0), null, out _, out _, out _), "ticket capacity reached early");
}
Assert(!boundedTickets.TryCreate("test", new CallbackSource(_ => { }, 0), null, out _, out string limitError, out _), "ticket capacity was exceeded");
Assert(limitError == IpcErrorCodes.DownloadTicketLimit, "wrong capacity error");
boundedTickets.Clear();
Console.WriteLine("PASS bounded ticket capacity");

static void Assert(bool condition, string message)
{
  if (!condition) throw new Exception(message);
}

static void AssertThrows<T>(Action action) where T : Exception
{
  try { action(); }
  catch (T) { return; }
  throw new Exception("Expected " + typeof(T).Name);
}

sealed class CallbackSource : IpcDownloadSource
{
  internal CallbackSource(Action<Stream> callback, long length)
    : base(callback, length, "sample.wav") { }
}
