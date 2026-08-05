export {
  AdofaiIpcClient,
  AdofaiIpcNamespaceClient,
  tryConnect
} from "./client";

export {
  AdofaiIpcError,
  IpcConnectionError,
  IpcHttpError,
  IpcResponseError,
  IpcTimeoutError,
  isIpcUnavailable
} from "./errors";

export type {
  IpcConnectionErrorCode,
  IpcConnectionErrorOptions
} from "./errors";

export type {
  AdofaiIpcClientOptions,
  IpcCallOptions,
  IpcErrorInfo,
  IpcErrorResponse,
  IpcHealthResponse,
  IpcNamespaceDetail,
  IpcNamespacesResponse,
  IpcNamespaceSummary,
  IpcRequestId,
  IpcResponse,
  IpcSuccessResponse,
  TryConnectOptions
} from "./types";
