import { IpcCallError } from "../../api/errors";
import type { FileReader } from "../../ports/file-reader";

/** Stream through a bounded queue; never buffer the full file in the transport. */
export const readHttpFile: FileReader = ({ url, size, signal }) => {
  const cancellation = new AbortController();
  let outputController: ReadableStreamDefaultController<Uint8Array>;
  const abort = () => {
    cancellation.abort(signal.reason); cleanup();
    void body?.cancel(signal.reason).catch(() => {});
    outputController.error(signal.reason);
  };
  const cleanup = () => signal.removeEventListener("abort", abort);
  let body: ReadableStreamDefaultReader<Uint8Array> | undefined;
  let received = 0;
  return new ReadableStream<Uint8Array>({
    start(output) {
      outputController = output;
      signal.addEventListener("abort", abort, { once: true });
      if (signal.aborted) abort();
    },
    async pull(output) {
      try {
        if (cancellation.signal.aborted) throw cancellation.signal.reason;
        if (!body) {
          const response = await fetch(url, { signal: cancellation.signal, redirect: "error", credentials: "omit", cache: "no-store", referrerPolicy: "no-referrer" });
          if (!response.ok || !response.body) {
            await response.body?.cancel();
            throw new IpcCallError(response.status === 404 ? "file_expired" : "file_transfer_failed", "The file is unavailable. Request a new file and try again.");
          }
          const declared = response.headers.get("X-Byte-Length");
          if (declared === null || !/^\d+$/.test(declared) || Number(declared) !== size) {
            await response.body.cancel(); throw new IpcCallError("invalid_file", "The file length does not match its IPC response.");
          }
          body = response.body.getReader();
        }
        const chunk = await body.read();
        if (cancellation.signal.aborted) throw cancellation.signal.reason;
        if (chunk.done) {
          if (received !== size) throw new IpcCallError("invalid_file", "The file transfer ended before all bytes arrived.");
          cleanup(); output.close(); return;
        }
        received += chunk.value.byteLength;
        if (received > size) throw new IpcCallError("invalid_file", "The file transfer exceeded its declared length.");
        output.enqueue(chunk.value);
      } catch (error) {
        cancellation.abort(error); await body?.cancel(error).catch(() => {}); cleanup();
        output.error(error instanceof IpcCallError || signal.aborted ? (signal.aborted ? signal.reason : error)
          : new IpcCallError("file_transfer_failed", error instanceof Error ? error.message : "The file transfer failed."));
      }
    },
    async cancel(reason) { cancellation.abort(reason); try { await body?.cancel(reason); } finally { cleanup(); } },
  }, { highWaterMark: 0 });
};
