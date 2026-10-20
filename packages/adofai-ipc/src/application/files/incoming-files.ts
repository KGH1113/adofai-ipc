import { ReceivedFile } from "../../api/received-file";
import { IpcCallError } from "../../api/errors";
import type { FileReader } from "../../ports/file-reader";
import type { FileDownloader } from "../../ports/file-downloader";
import { decodeFileReference } from "../../schemas/file-reference";

export class IncomingFiles {
  private lifetime = new AbortController();
  constructor(private readonly reader: FileReader | undefined, private readonly origin: () => string | undefined,
    private readonly downloader?: FileDownloader) {}
  decode = (value: unknown): ReceivedFile => {
    const file = decodeFileReference(value, this.origin());
    const lifetime = this.lifetime.signal;
    return new ReceivedFile(file.fileName, file.contentType, file.byteLength, options => {
      if (lifetime.aborted) throw lifetime.reason;
      if (!this.reader) throw new IpcCallError("file_transfer_unavailable", "A file reader is required by this IPC transport.");
      const controller = new AbortController();
      let reader: ReadableStreamDefaultReader<Uint8Array> | undefined;
      let outputController: ReadableStreamDefaultController<Uint8Array> | undefined;
      const signals = [lifetime, options.signal].filter((signal): signal is AbortSignal => signal !== undefined);
      const listeners: Array<() => void> = [];
      const cleanup = () => {
        listeners.splice(0).forEach(off => off());
        controller.signal.removeEventListener("abort", abortRead);
      };
      const abortRead = () => {
        cleanup();
        void reader?.cancel(controller.signal.reason).catch(() => {});
        outputController?.error(controller.signal.reason);
      };
      controller.signal.addEventListener("abort", abortRead, { once: true });
      for (const signal of signals) {
        const abort = () => controller.abort(signal.reason);
        if (signal.aborted) { abort(); break; }
        signal.addEventListener("abort", abort, { once: true });
        listeners.push(() => signal.removeEventListener("abort", abort));
      }
      try { reader = this.reader({ url: file.url, size: file.byteLength, signal: controller.signal }).getReader(); }
      catch (error) { cleanup(); throw error; }
      return new ReadableStream<Uint8Array>({
        start(output) { outputController = output; if (controller.signal.aborted) abortRead(); },
        async pull(output) {
          try {
            const chunk = await reader!.read();
            if (controller.signal.aborted) throw controller.signal.reason;
            if (chunk.done) { cleanup(); output.close(); }
            else output.enqueue(chunk.value);
          } catch (error) { cleanup(); output.error(error); }
        },
        async cancel(reason) { try { await reader!.cancel(reason); } finally { cleanup(); } },
      }, { highWaterMark: 0 });
    }, this.downloader ? () => {
      if (lifetime.aborted) throw lifetime.reason;
      this.downloader!({ url: file.url, signal: lifetime });
    } : undefined);
  };
  invalidate(reason = new IpcCallError("unavailable", "The file's app session disconnected. Request a new file after reconnecting.")): void {
    this.lifetime.abort(reason); this.lifetime = new AbortController();
  }
}
