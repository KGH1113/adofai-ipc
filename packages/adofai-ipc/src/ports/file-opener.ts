export interface FileOpenRequest { url: string; signal?: AbortSignal }
/** Bytes from start (inclusive) to end (exclusive) of an opened file. */
export interface FileRangeRequest { url: string; start: number; end: number; signal?: AbortSignal }

/** The runtime's service for files opened for repeated reads at any position. */
export interface FileOpener {
  /** Turns a file's download URL into an opened file URL. A rejection leaves the file unread. */
  open(request: FileOpenRequest): Promise<string>;
  read(request: FileRangeRequest): ReadableStream<Uint8Array>;
  /** Releases an opened file. Never rejects; the runtime also closes idle files. */
  close(url: string): Promise<void>;
}
