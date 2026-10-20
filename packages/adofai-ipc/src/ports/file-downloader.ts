/** A browser handoff, not a content reader. The session signal owns navigation cleanup. */
export interface FileDownloadRequest { url: string; signal: AbortSignal }
export type FileDownloader = (request: FileDownloadRequest) => void;
