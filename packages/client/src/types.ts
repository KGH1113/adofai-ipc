export type IpcRequestId = string | number | null;

export interface IpcCallOptions<TParams = unknown> {
  namespace: string;
  method: string;
  params?: TParams;
  id?: IpcRequestId;
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
  protocolVersion: number;
  port: number;
}

export interface IpcNamespaceSummary {
  name: string;
  displayName: string;
  version: string;
}

export interface IpcNamespacesResponse {
  namespaces: IpcNamespaceSummary[];
}

export interface IpcNamespaceDetail {
  namespace: string;
  displayName: string;
  version: string;
  methods: string[];
}

export interface AdofaiIpcClientOptions {
  baseUrl?: string;
  fetch?: typeof fetch;
  timeoutMs?: number;
}

export interface TryConnectOptions {
  host?: string;
  startPort?: number;
  endPort?: number;
  fetch?: typeof fetch;
  timeoutMs?: number;
}
