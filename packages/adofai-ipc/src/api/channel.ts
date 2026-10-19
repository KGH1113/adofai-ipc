import type { IpcCallError } from "./errors";

export type ChannelStatus = "unavailable" | "initializing" | "ready" | "error";
export type Channel<M extends object> = M & {
  readonly status: ChannelStatus;
  readonly sessionId: string | undefined;
  ready(): Promise<void>;
  onStatus(listener: (status: ChannelStatus, error?: IpcCallError) => void): () => void;
};
