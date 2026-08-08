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
  IpcVersionMismatchError,
  isIpcUnavailable
} from "./errors";

export type {
  IpcConnectionErrorCode,
  IpcConnectionErrorOptions,
  IpcVersionMismatchDirection
} from "./errors";

export { CLIENT_VERSION } from "./version";

export type {
  AdofaiIpcClientOptions,
  IpcCallOptions,
  IpcErrorInfo,
  IpcErrorResponse,
  IpcHealthResponse,
  IpcNamespaceDetail,
  IpcNamespaceErrorInfo,
  IpcNamespaceStatus,
  IpcNamespacesResponse,
  IpcNamespaceSummary,
  IpcRequestId,
  IpcRequestOptions,
  IpcResponse,
  IpcSuccessResponse,
  TryConnectOptions,
  WaitForNamespaceOptions
} from "./types";
