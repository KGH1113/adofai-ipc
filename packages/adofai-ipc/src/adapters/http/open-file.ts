import { IpcCallError } from "../../api/errors";
import type { FileOpener } from "../../ports/file-opener";

const requestOptions = { redirect: "error", credentials: "omit", cache: "no-store", referrerPolicy: "no-referrer" } as const;
const knownCodes = new Map<string, string>([
  ["file_not_seekable", "This file can only be read once, from the start. Read it with stream() or download() instead."],
  ["file_expired", "The file is unavailable. Request a new file and try again."],
  ["file_open_limit", "Too many files are open in the game. Close one and try again."],
  ["origin_not_allowed", "This page is not allowed to open the file."],
]);
const unavailable = () => new IpcCallError("file_open_unavailable",
  "The file could not be opened. The game's IPC runtime may be older than 2.2.0, or the file expired. Read it with stream() or download() instead.");

/** The opened file URL must stay on the runtime that sent the file. */
function isOpenedFileUrl(value: unknown, downloadUrl: string): value is string {
  if (typeof value !== "string") return false;
  let url: URL;
  try { url = new URL(value); } catch { return false; }
  return url.origin === new URL(downloadUrl).origin && !url.username && !url.password && !url.search && !url.hash
    && /^\/ipc\/files\/[A-Za-z0-9_-]{43}$/.test(url.pathname);
}

async function readCode(response: Response): Promise<{ code?: unknown; url?: unknown } | undefined> {
  try { return await response.json() as { code?: unknown; url?: unknown }; } catch { return undefined; }
}

/** Opens files through the runtime's HTTP file service. Content is never buffered here. */
export const httpFileOpener: FileOpener = {
  async open({ url, signal }) {
    let response: Response;
    try { response = await fetch(url.replace("/ipc/download/", "/ipc/files/open/"), { ...requestOptions, method: "POST", signal }); }
    catch (error) {
      if (signal?.aborted) throw signal.reason;
      // An older runtime has no file service, and the browser hides why a cross-origin request failed.
      throw unavailable();
    }
    const body = await readCode(response);
    if (response.ok) {
      const opened = body?.url;
      if (!isOpenedFileUrl(opened, url)) throw new IpcCallError("invalid_file", "Invalid opened file response from the connected IPC runtime.");
      return opened;
    }
    const code = typeof body?.code === "string" ? body.code : undefined;
    const message = code === undefined ? undefined : knownCodes.get(code);
    if (code !== undefined && message !== undefined) throw new IpcCallError(code, message);
    throw unavailable();
  },

  read({ url, start, end, signal }) {
    const cancellation = new AbortController();
    const abort = () => cancellation.abort(signal?.reason);
    signal?.addEventListener("abort", abort, { once: true });
    const cleanup = () => signal?.removeEventListener("abort", abort);
    const expected = end - start;
    let body: ReadableStreamDefaultReader<Uint8Array> | undefined;
    let received = 0;
    return new ReadableStream<Uint8Array>({
      async pull(output) {
        try {
          if (cancellation.signal.aborted) throw cancellation.signal.reason;
          if (expected === 0) { cleanup(); output.close(); return; }
          if (!body) {
            const response = await fetch(url, { ...requestOptions, signal: cancellation.signal, headers: { Range: `bytes=${start}-${end - 1}` } });
            const range = response.headers.get("Content-Range");
            const match = range && /^bytes (\d+)-(\d+)\/\d+$/.exec(range);
            if (response.status !== 206 || !response.body || !match || Number(match[1]) !== start || Number(match[2]) !== end - 1) {
              await response.body?.cancel();
              throw response.status === 404 ? new IpcCallError("file_expired", "The opened file was closed or expired. Request the file again.")
                : new IpcCallError("file_transfer_failed", "The file range could not be read.");
            }
            body = response.body.getReader();
          }
          const chunk = await body.read();
          if (cancellation.signal.aborted) throw cancellation.signal.reason;
          if (chunk.done) {
            if (received !== expected) throw new IpcCallError("invalid_file", "The file range ended before all bytes arrived.");
            cleanup(); output.close(); return;
          }
          received += chunk.value.byteLength;
          if (received > expected) throw new IpcCallError("invalid_file", "The file range exceeded its requested length.");
          output.enqueue(chunk.value);
        } catch (error) {
          const failure = signal?.aborted ? signal.reason : error instanceof IpcCallError ? error
            : new IpcCallError("file_transfer_failed", error instanceof Error ? error.message : "The file range could not be read.");
          cancellation.abort(failure); await body?.cancel(failure).catch(() => {}); cleanup();
          output.error(failure);
        }
      },
      async cancel(reason) { cancellation.abort(reason); try { await body?.cancel(reason); } finally { cleanup(); } },
    }, { highWaterMark: 0 });
  },

  async close(url) {
    // keepalive lets a page close its files while it unloads.
    try { await fetch(`${url}/close`, { ...requestOptions, method: "POST", keepalive: true }); } catch { /* The runtime closes idle files. */ }
  },
};
