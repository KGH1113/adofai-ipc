export type IpcRequestId = string | number | null;

export interface IpcCallOptions<TParams = unknown> {
  namespace: string;
  method: string;
  params?: TParams;
  id?: IpcRequestId;
  timeoutMs?: number;
}

export interface IpcRequestOptions {
  timeoutMs?: number;
}

export interface WaitForNamespaceOptions {
  timeoutMs?: number;
  pollIntervalMs?: number;
  requestTimeoutMs?: number;
  status?: "registered" | "ready";
}

export interface IpcSuccessResponse<TResult = unknown> {
  ok: true;
  result: TResult;
  id?: IpcRequestId;
}

export interface IpcErrorInfo {
  code: string;
  message: string;
}

export interface IpcErrorResponse {
  ok: false;
  result?: null;
  error: IpcErrorInfo;
  id?: IpcRequestId;
}

export type IpcResponse<TResult = unknown> =
  | IpcSuccessResponse<TResult>
  | IpcErrorResponse;

export interface IpcHealthResponse {
  ok: true;
  server: "AdofaiIpc";
  serverVersion?: string;
  protocolVersion: number;
  port: number;
}

export type IpcNamespaceStatus = "initializing" | "ready" | "error";

export interface IpcNamespaceErrorInfo {
  code: string;
  message: string;
}

export interface IpcNamespaceSummary {
  name: string;
  displayName: string;
  version: string;
  status: IpcNamespaceStatus;
}

export interface IpcNamespacesResponse {
  namespaces: IpcNamespaceSummary[];
}

export interface IpcNamespaceDetail {
  namespace: string;
  displayName: string;
  version: string;
  status: IpcNamespaceStatus;
  error?: IpcNamespaceErrorInfo | null;
  methods: string[];
}

export interface AdofaiIpcClientOptions {
  baseUrl?: string;
  fetch?: typeof fetch;
  requestTimeoutMs?: number;
  /** @deprecated Use requestTimeoutMs instead. */
  timeoutMs?: number;
}

export interface TryConnectOptions {
  host?: string;
  startPort?: number;
  endPort?: number;
  fetch?: typeof fetch;
  probeTimeoutMs?: number;
  requestTimeoutMs?: number;
  onVersionMismatch?: (error: import("./errors").IpcVersionMismatchError) => void;
  /** @deprecated Use probeTimeoutMs and requestTimeoutMs instead. */
  timeoutMs?: number;
}
