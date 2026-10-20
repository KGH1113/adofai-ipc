export interface FileReadOptions { signal?: AbortSignal }

/** A single-use file received from a mod. Reading starts only when its content is consumed. */
export class ReceivedFile {
  private consumed = false;
  constructor(readonly name: string, readonly type: string, readonly size: number,
    private readonly read: (options: FileReadOptions) => ReadableStream<Uint8Array>) {}
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
}
