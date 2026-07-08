import type { IpcErrorInfo } from "./types";

export class AdofaiIpcError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "AdofaiIpcError";
  }
}

export class IpcConnectionError extends AdofaiIpcError {
  constructor(message = "Could not connect to AdofaiIpc.") {
    super(message);
    this.name = "IpcConnectionError";
  }
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
