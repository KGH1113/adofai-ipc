import type { IpcTransportFactory } from "../ports/transport";

export interface ConnectIpcOptions {
  host?: "127.0.0.1" | "localhost";
  startPort?: number;
  endPort?: number;
  connectTimeoutMs?: number;
  /** Check idle connections and replace sockets that no longer answer, even without a close event. */
  heartbeatIntervalMs?: number;
  heartbeatTimeoutMs?: number;
  transportFactory?: IpcTransportFactory;
  onProtocolMismatch?: (error: import("./connection-errors").IpcProtocolMismatchError) => void;
}
export interface IpcCommandOptions { id?: string; signal?: AbortSignal }
export interface IpcReadyOptions { timeoutMs?: number; signal?: AbortSignal }

export interface AppOptions { displayName: string }
export interface IpcOptions extends ConnectIpcOptions { requestTimeoutMs?: number }
