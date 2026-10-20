import type { IpcCommandOptions, IpcReadyOptions } from "../api/options";
import type { IpcConnectionState, IpcErrorInfo, IpcMessage, IpcNamespaceStatus } from "../models/messaging";

/** Application-owned boundary; registering an app does not require a WebSocket implementation. */
export interface MessageSession {
  readonly state: IpcConnectionState;
  readonly connectionId: string | undefined;
  readonly downloadOrigin?: string;
  start(): Promise<void>;
  close(): void;
  checkConnection(): void;
  onState(listener: (state: IpcConnectionState) => void): () => void;
  namespace(name: string, protocolMajor?: number): MessageChannel;
}
export interface MessageChannel {
  on<T = unknown>(name: string, listener: (payload: T, message: IpcMessage<T>) => void): () => void;
  onCommand<T = unknown>(name: string, listener: (payload: T, message: IpcMessage<T>) => void): () => void;
  onStatus(listener: (status: IpcNamespaceStatus, error?: IpcErrorInfo) => void): () => void;
  whenReady(options?: IpcReadyOptions): Promise<void>;
  send(name: string, payload?: unknown, options?: IpcCommandOptions): string;
  reply(correlationId: string, name: string, payload?: unknown): string;
}
