import { IpcCallError } from "./errors";
import type { OpenedFile } from "./opened-file";

export interface FileReadOptions { signal?: AbortSignal }

/** A single-use file received from a mod. Reading starts only when its content is consumed. */
export class ReceivedFile {
  private consumed = false;
  constructor(readonly name: string, readonly type: string, readonly size: number,
    private readonly read: (options: FileReadOptions) => ReadableStream<Uint8Array>,
    private readonly startDownload?: () => void,
    private readonly openFile?: (options: FileReadOptions) => Promise<OpenedFile>) {}
  get used(): boolean { return this.consumed; }
  stream(options: FileReadOptions = {}): ReadableStream<Uint8Array> {
    if (this.consumed) throw new TypeError("This IPC file has already been read. Request a new file to read it again.");
    if (options.signal?.aborted) throw options.signal.reason;
    this.consumed = true;
    return this.read(options);
  }
  arrayBuffer(options?: FileReadOptions): Promise<ArrayBuffer> { return new Response(this.stream(options)).arrayBuffer(); }
  text(options?: FileReadOptions): Promise<string> { return new Response(this.stream(options)).text(); }
  async blob(options?: FileReadOptions): Promise<Blob> { return new Blob([await this.arrayBuffer(options)], { type: this.type }); }

  /** Hand this file to the browser's download manager without reading its contents in JavaScript.
   * Resolves after requesting the download, not after the file has been saved.
   * The browser owns progress and cancellation; the IPC session must remain connected.
   */
  async download(): Promise<void> {
    if (this.consumed) throw new TypeError("This IPC file has already been read. Request a new file to read it again.");
    if (!this.startDownload) throw new IpcCallError("file_download_unavailable", "Browser downloads are unavailable in this environment. Read the file as a stream instead.");
    this.consumed = true;
    this.startDownload();
  }

  /** Open this file for repeated reads at any position without loading it into memory.
   * Give the URL to a media element or an image, or read byte ranges with read(). Close it when done.
   * Rejects with file_not_seekable or file_open_unavailable and leaves the file unread,
   * so it can still be read with stream() or download().
   */
  async open(options: FileReadOptions = {}): Promise<OpenedFile> {
    if (this.consumed) throw new TypeError("This IPC file has already been read. Request a new file to read it again.");
    if (!this.openFile) throw new IpcCallError("file_open_unavailable", "Opening files is unavailable in this environment. Read the file as a stream instead.");
    if (options.signal?.aborted) throw options.signal.reason;
    this.consumed = true;
    try { return await this.openFile(options); }
    catch (error) { this.consumed = false; throw error; }
  }
}
