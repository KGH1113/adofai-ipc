export interface FileReadRequest { url: string; size: number; signal: AbortSignal }
export type FileReader = (request: FileReadRequest) => ReadableStream<Uint8Array>;
