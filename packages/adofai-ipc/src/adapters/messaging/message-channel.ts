import { IpcNamespaceError, IpcTimeoutError } from "../../api/connection-errors";
import type { IpcCommandOptions, IpcReadyOptions } from "../../api/options";
import type { IpcErrorInfo, IpcMessage, IpcNamespaceStatus } from "../../models/messaging";
import type { MessageConnection } from "./message-connection";
type Listener = (payload: unknown, message: IpcMessage<unknown>) => void;
export class IpcChannel {
  private readonly events = new Map<string, Set<Listener>>();
  private readonly commands = new Map<string, Set<Listener>>();
  private readonly statuses = new Set<(status: IpcNamespaceStatus, error?: IpcErrorInfo) => void>();
  private _status: IpcNamespaceStatus = "unavailable";
  private _error: IpcErrorInfo | undefined;
  constructor(private readonly connection: MessageConnection, readonly name: string, readonly protocolMajor?: number) {}
  get status(): IpcNamespaceStatus { return this._status; }
  get error(): IpcErrorInfo | undefined { return this._error; }
  on<T = unknown>(name: string, listener: (payload: T, message: IpcMessage<T>) => void): () => void { return this.listen(this.events, name, listener); }
  onCommand<T = unknown>(name: string, listener: (payload: T, message: IpcMessage<T>) => void): () => void { return this.listen(this.commands, name, listener); }
  onStatus(listener: (status: IpcNamespaceStatus, error?: IpcErrorInfo) => void): () => void { this.statuses.add(listener); listener(this.status, this.error); return () => this.statuses.delete(listener); }
  send(name: string, payload?: unknown, options?: IpcCommandOptions): string { this.requireCompatible(); return this.connection.send("command", this.name, name, payload, options); }
  emit(name: string, payload?: unknown): string { this.requireCompatible(); return this.connection.send("event", this.name, name, payload); }
  reply(correlationId: string, name: string, payload?: unknown): string { this.requireCompatible(); return this.connection.send("event", this.name, name, payload, {}, correlationId); }
  whenReady(options: IpcReadyOptions = {}): Promise<void> {
    if (options.signal?.aborted) return Promise.reject(options.signal.reason ?? new DOMException("Aborted", "AbortError"));
    if (this.status === "ready") return Promise.resolve();
    if (this.status === "error") return Promise.reject(new IpcNamespaceError(this.error?.code ?? "namespace_error", this.error?.message ?? "The mod could not initialize."));
    return new Promise((resolve, reject) => {
      let timer: ReturnType<typeof setTimeout> | undefined; let off = () => {};
      const finish = (error?: unknown) => { clearTimeout(timer); off(); options.signal?.removeEventListener("abort", abort); if (error) reject(error); else resolve(); };
      const abort = () => finish(options.signal?.reason ?? new DOMException("Aborted", "AbortError"));
      options.signal?.addEventListener("abort", abort, { once: true });
      off = this.onStatus((status, error) => { if (status === "ready") finish(); else if (status === "error") finish(new IpcNamespaceError(error?.code ?? "namespace_error", error?.message ?? "The mod could not initialize.")); });
      timer = setTimeout(() => finish(new IpcTimeoutError(options.timeoutMs ?? 15000)), options.timeoutMs ?? 15000);
    });
  }
  /** Internal transport adapter dispatch. */
  receive(message: IpcMessage): void {
    if (message.name === "command.rejected") {
      const error = message.payload as IpcErrorInfo | undefined;
      if (error?.code === "origin_not_allowed") {
        this._status = "error"; this._error = error;
        for (const listener of [...this.statuses]) { try { listener(this.status, this.error); } catch { } }
      }
    }
    if (message.kind === "namespace") {
      let payload = message.payload as { status: IpcNamespaceStatus; error?: IpcErrorInfo; featureProtocolMajor?: number };
      if (payload?.status === "ready" && this.protocolMajor !== undefined && payload.featureProtocolMajor !== this.protocolMajor) {
        payload = { status: "error", error: { code: "feature_protocol_mismatch", message: "The app and mod use different message versions. Update both, restart ADOFAI, and reconnect." } };
      }
      if (["ready", "initializing", "error", "unavailable"].includes(payload?.status)) { this._status = payload.status; this._error = payload.error; for (const listener of [...this.statuses]) { try { listener(this.status, this.error); } catch { } } }
    }
    if (this._error?.code === "feature_protocol_mismatch") return;
    const listeners = (message.kind === "command" ? this.commands : this.events).get(message.name);
    if (listeners) for (const listener of [...listeners]) { try { listener(message.payload, message); } catch { } }
  }
  disconnected(): void { this._status = "unavailable"; this._error = undefined; for (const listener of [...this.statuses]) { try { listener(this.status); } catch { } } }
  private requireCompatible(): void {
    if (this._error?.code === "feature_protocol_mismatch") throw new IpcNamespaceError(this._error.code, this._error.message);
  }
  private listen<T>(map: Map<string, Set<Listener>>, name: string, listener: (payload: T, message: IpcMessage<T>) => void): () => void {
    let listeners = map.get(name); if (!listeners) { listeners = new Set(); map.set(name, listeners); }
    const stored = listener as Listener; listeners.add(stored);
    return () => { listeners?.delete(stored); if (!listeners?.size) map.delete(name); };
  }
}
