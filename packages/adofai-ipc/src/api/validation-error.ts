export class IpcValidationError extends TypeError {
  readonly code = "invalid_value";
  constructor(readonly path: string, message: string) {
    super(`${path}: ${message}`);
    this.name = "IpcValidationError";
  }
}
