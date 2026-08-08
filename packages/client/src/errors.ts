import type { IpcErrorInfo } from "./types";

export type IpcConnectionErrorCode = "UNAVAILABLE" | "TIMEOUT";
export type IpcVersionMismatchDirection =
  | "server_outdated"
  | "client_outdated"
  | "legacy_server";

export interface IpcConnectionErrorOptions {
  code?: IpcConnectionErrorCode;
  cause?: unknown;
}

export class AdofaiIpcError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AdofaiIpcError";
  }
}

export class IpcConnectionError extends AdofaiIpcError {
  readonly code: IpcConnectionErrorCode;
  readonly cause?: unknown;

  constructor(
    message = "Could not connect to AdofaiIpc.",
    options: IpcConnectionErrorOptions = {}
  ) {
    super(message);
    this.name = "IpcConnectionError";
    this.code = options.code ?? "UNAVAILABLE";
    this.cause = options.cause;
  }
}

export class IpcTimeoutError extends IpcConnectionError {
  readonly timeoutMs: number;

  constructor(timeoutMs: number, options: Pick<IpcConnectionErrorOptions, "cause"> = {}) {
    super(`AdofaiIpc request timed out after ${timeoutMs} ms.`, {
      code: "TIMEOUT",
      cause: options.cause
    });
    this.name = "IpcTimeoutError";
    this.timeoutMs = timeoutMs;
  }
}

export class IpcVersionMismatchError extends AdofaiIpcError {
  readonly code = "VERSION_MISMATCH" as const;
  readonly clientVersion: string;
  readonly serverVersion: string | null;
  readonly direction: IpcVersionMismatchDirection;
  readonly protocolVersion: number | null;

  constructor(options: {
    clientVersion: string;
    serverVersion: string | null;
    direction: IpcVersionMismatchDirection;
    protocolVersion: number | null;
  }) {
    const server = options.serverVersion ?? "legacy/unknown";
    super(`AdofaiIpc version mismatch: client ${options.clientVersion}, server ${server}.`);
    this.name = "IpcVersionMismatchError";
    this.clientVersion = options.clientVersion;
    this.serverVersion = options.serverVersion;
    this.direction = options.direction;
    this.protocolVersion = options.protocolVersion;
  }
}

export function isIpcUnavailable(error: unknown): error is IpcConnectionError {
  return error instanceof IpcConnectionError && error.code === "UNAVAILABLE";
}

export class IpcHttpError extends AdofaiIpcError {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "IpcHttpError";
    this.status = status;
  }
}

export class IpcResponseError extends AdofaiIpcError {
  readonly code: string;
  readonly error: IpcErrorInfo;

  constructor(error: IpcErrorInfo) {
    super(error.message);
    this.name = "IpcResponseError";
    this.code = error.code;
    this.error = error;
  }
}
