export type IpcConnectionState = "connected" | "reconnecting" | "disconnected" | "closed" | "incompatible";
export type IpcNamespaceStatus = "initializing" | "ready" | "error" | "unavailable";
export interface IpcErrorInfo { code: string; message: string }
export interface IpcMessage<TPayload = unknown> {
  kind: string;
  namespace?: string | null;
  name: string;
  id: string;
  correlationId?: string;
  payload: TPayload;
}
