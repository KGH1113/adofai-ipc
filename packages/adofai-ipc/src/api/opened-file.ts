import { IpcCallError } from "./errors";

/** Bytes from start (inclusive) to end (exclusive), like Blob.slice. Both default to the whole file. */
export interface FileRange { start?: number; end?: number; signal?: AbortSignal }

/**
 * A received file opened for repeated reads at any position. Its URL works directly in
 * media elements, images and links, which load only the parts they need. The file stays
 * open in the game until it is closed, its app session ends, or nothing reads it for 30 minutes.
 */
export class OpenedFile {
  private isClosed = false;
  constructor(readonly url: string, readonly name: string, readonly type: string, readonly size: number,
    private readonly readRange: (start: number, end: number, signal?: AbortSignal) => ReadableStream<Uint8Array>,
    private readonly release: () => Promise<void>) {}
  get closed(): boolean { return this.isClosed; }

  /** Streams a byte range without loading the rest of the file. It can be called any number of times. */
  read(range: FileRange = {}): ReadableStream<Uint8Array> {
    if (this.isClosed) throw new IpcCallError("file_closed", "This file was closed. Request a new file to read it again.");
    const start = range.start ?? 0;
    const end = range.end ?? this.size;
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 0 || end < start || end > this.size)
      throw new RangeError(`A file range needs 0 ≤ start ≤ end ≤ ${this.size}.`);
    if (range.signal?.aborted) throw range.signal.reason;
    return this.readRange(start, end, range.signal);
  }

  /** Releases the file in the game. Media elements using the URL stop loading. Calling it again does nothing. */
  async close(): Promise<void> {
    if (this.isClosed) return;
    this.isClosed = true;
    await this.release();
  }
}
