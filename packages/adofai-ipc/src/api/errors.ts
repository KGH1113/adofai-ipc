export class IpcCallError extends Error {
  constructor(readonly code: string, message: string) { super(message); this.name = "IpcCallError"; }
}
