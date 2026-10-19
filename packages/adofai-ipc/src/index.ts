import { IpcConnection, type ConnectIpcOptions, type IpcChannel, type IpcMessage } from "@adofai-ipc/client";
import { decodeValue, IpcValidationError, type Models, type ValueSchema } from "./schema";

export { IpcValidationError, type ValueSchema, type Models } from "./schema";

export interface MethodDefinition {
  readonly wireName: string;
  readonly input: ValueSchema;
  readonly output: ValueSchema;
}
export interface ContractDefinition {
  readonly id: string;
  readonly hash: string;
  readonly models: Models;
  readonly mod: Readonly<Record<string, MethodDefinition>>;
  readonly app: Readonly<Record<string, MethodDefinition>>;
}
declare const contractTypes: unique symbol;
export interface IpcContract<M extends object, H extends object> extends ContractDefinition {
  readonly [contractTypes]?: { readonly mod: M; readonly handlers: H };
}
export type ChannelStatus = "unavailable" | "initializing" | "ready" | "error";
export type Channel<M extends object> = M & {
  readonly status: ChannelStatus;
  readonly sessionId: string | undefined;
  ready(): Promise<void>;
  onStatus(listener: (status: ChannelStatus, error?: IpcCallError) => void): () => void;
};

export function defineContract<M extends object, H extends object>(definition: ContractDefinition): IpcContract<M, H> {
  if (!/^[a-z0-9-]+$/.test(definition.id) || !/^[a-f0-9]{64}$/.test(definition.hash)) throw new TypeError("Invalid IPC contract identity.");
  function freeze<T>(value: T): T {
    if (value && typeof value === "object") {
      for (const item of Object.values(value)) freeze(item);
      Object.freeze(value);
    }
    return value;
  }
  return freeze(definition);
}

export class IpcCallError extends Error {
  constructor(readonly code: string, message: string) { super(message); this.name = "IpcCallError"; }
}

export interface AppOptions { displayName: string }
export interface IpcOptions extends ConnectIpcOptions { requestTimeoutMs?: number }
interface Pending {
  resolve(value: unknown): void;
  reject(error: unknown): void;
  output: ValueSchema;
  timer: ReturnType<typeof setTimeout>;
}
const registrationOutput: ValueSchema = { object: { appId: "string", sessionId: "string" } };

function uuid(): string {
  if (globalThis.crypto?.randomUUID) return globalThis.crypto.randomUUID();
  const bytes = new Uint8Array(16);
  if (!globalThis.crypto?.getRandomValues) throw new Error("A cryptographic random source is required to register an IPC app.");
  globalThis.crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6]! & 15) | 64; bytes[8] = (bytes[8]! & 63) | 128;
  const hex = [...bytes].map(value => value.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export class AppRegistration {
  readonly appId = uuid();
  readonly displayName: string;
  private readonly connection: IpcConnection;
  private readonly bindings = new Map<string, ContractBinding>();
  private readonly lifetime = new AbortController();
  private readonly timeoutMs: number;
  private readonly offState: () => void;
  constructor(options: AppOptions, settings: IpcOptions = {}) {
    if (!options || typeof options.displayName !== "string" || !options.displayName.trim() || options.displayName.length > 200)
      throw new TypeError("A displayName of 1–200 characters is required.");
    this.displayName = options.displayName;
    this.timeoutMs = settings.requestTimeoutMs ?? 30000;
    if (!Number.isFinite(this.timeoutMs) || this.timeoutMs <= 0) throw new TypeError("Invalid IPC request timeout.");
    this.connection = new IpcConnection(settings);
    this.offState = this.connection.onState(state => {
      if (state === "incompatible") this.lifetime.abort(new IpcCallError("protocol_mismatch", "The IPC runtime uses an incompatible protocol."));
    });
  }

  use<M extends object, H extends object>(contract: IpcContract<M, H>, handlers: NoInfer<H>): Channel<M> {
    this.check();
    if (this.bindings.has(contract.id)) throw new Error(`This app already uses ${contract.id}.`);
    if (!handlers || typeof handlers !== "object") throw new TypeError("App handlers must be an object.");
    const implementations = handlers as Record<string, unknown>;
    for (const name of Object.keys(contract.app)) {
      if (!Object.hasOwn(implementations, name) || typeof implementations[name] !== "function") throw new TypeError(`Missing app handler: ${name}.`);
    }
    for (const name of Object.keys(implementations)) {
      if (!Object.hasOwn(contract.app, name)) throw new TypeError(`Unknown app handler: ${name}.`);
    }
    const binding = new ContractBinding(this, this.connection, contract, implementations, this.lifetime.signal, this.timeoutMs);
    this.bindings.set(contract.id, binding);
    void this.connection.start().catch(() => {});
    return binding.api as Channel<M>;
  }

  dispose(): void {
    if (this.lifetime.signal.aborted && this.connection.state === "closed") return;
    this.lifetime.abort(new IpcCallError("disposed", "The app registration has been disposed."));
    for (const binding of this.bindings.values()) binding.dispose();
    this.bindings.clear(); this.offState(); this.connection.close();
  }
  /** Recheck app availability when the page returns from suspension. */
  checkConnection(): void { this.check(); this.connection.checkConnection(); }
  private check(): void { if (this.lifetime.signal.aborted) throw this.lifetime.signal.reason; }
}

class ContractBinding {
  readonly api: object;
  private readonly raw: IpcChannel;
  private readonly pending = new Map<string, Pending>();
  private readonly cleanup: Array<() => void> = [];
  private registration: { connection: string; promise: Promise<void> } | undefined;
  private readonly statuses = new Set<(status: ChannelStatus, error?: IpcCallError) => void>();
  private status: ChannelStatus = "unavailable";
  private statusError: IpcCallError | undefined;
  private sessionId: string | undefined;
  private epoch = 0;
  private disposed = false;
  constructor(private readonly app: AppRegistration, private readonly connection: IpcConnection,
    private readonly contract: ContractDefinition, private readonly handlers: Record<string, unknown>,
    private readonly signal: AbortSignal, private readonly timeoutMs: number) {
    this.raw = connection.namespace(contract.id, 1);
    const api: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
    api.ready = () => this.ready();
    api.onStatus = (listener: (status: ChannelStatus, error?: IpcCallError) => void) => {
      this.check();
      if (typeof listener !== "function") throw new TypeError("A status listener is required.");
      this.statuses.add(listener);
      try { listener(this.status, this.statusError); } catch { /* A consumer cannot break IPC dispatch. */ }
      return () => this.statuses.delete(listener);
    };
    Object.defineProperties(api, {
      status: { enumerable: true, get: () => this.status },
      sessionId: { enumerable: true, get: () => this.sessionId },
    });
    for (const [name, method] of Object.entries(contract.mod)) {
      if (["ready", "onStatus", "status", "sessionId", "then", "constructor"].includes(name)) throw new TypeError(`Reserved IPC method name: ${name}.`);
      api[name] = async (input?: unknown) => {
        const request = decodeValue(method.input, input === undefined ? {} : input, contract.models);
        await this.ready();
        return this.request(method.wireName, request, method.output);
      };
    }
    this.api = Object.freeze(api);
    this.cleanup.push(this.raw.on("rpc.result", (value, message) => this.receive(value, message, false)));
    for (const name of ["rpc.error", "command.rejected", "command.failed"]) {
      this.cleanup.push(this.raw.on(name, (value, message) => this.receive(value, message, true)));
    }
    for (const [name, method] of Object.entries(contract.app)) {
      this.cleanup.push(this.raw.onCommand(method.wireName, (payload, message) => { void this.handleApp(name, method, payload, message); }));
    }
    this.cleanup.push(this.raw.onStatus((status, error) => {
      if (status === "ready") {
        // Register without waiting for a first mod call so the mod can push its initial snapshot.
        void this.ready().catch(() => {});
      } else {
        this.epoch++; this.sessionId = undefined; this.registration = undefined;
        const reason = new IpcCallError(error?.code ?? "unavailable", error?.message ?? "The mod is no longer available.");
        this.failAll(reason); this.setStatus(status, status === "error" ? reason : undefined);
      }
    }));
    const abort = () => {
      this.epoch++; this.sessionId = undefined;
      this.failAll(signal.reason);
      this.setStatus("error", signal.reason instanceof IpcCallError ? signal.reason : new IpcCallError("disposed", "The app registration has been disposed."));
    };
    signal.addEventListener("abort", abort, { once: true });
    this.cleanup.push(() => signal.removeEventListener("abort", abort));
  }

  private check(): void {
    if (this.signal.aborted) throw this.signal.reason;
    if (this.disposed) throw new IpcCallError("disposed", "The channel has been disposed.");
  }
  private async ready(): Promise<void> {
    this.check();
    await this.raw.whenReady({ signal: this.signal });
    this.check();
    const connection = this.connection.connectionId;
    if (!connection) throw new IpcCallError("unavailable", "The app is disconnected.");
    if (!this.registration || this.registration.connection !== connection) {
      const epoch = this.epoch;
      this.setStatus("initializing");
      const promise = this.request("rpc.register", {
        appId: this.app.appId, displayName: this.app.displayName, contractHash: this.contract.hash,
      }, registrationOutput).then(value => {
        if (epoch !== this.epoch || connection !== this.connection.connectionId) throw new IpcCallError("unavailable", "The app registration has expired.");
        this.check();
        const registered = value as { appId: string; sessionId: string };
        if (registered.appId !== this.app.appId || !registered.sessionId) throw new IpcCallError("invalid_response", "Invalid app registration response.");
        this.sessionId = registered.sessionId; this.setStatus("ready");
      }).catch(error => {
        if (epoch === this.epoch && connection === this.connection.connectionId && !this.signal.aborted && !this.disposed)
          this.setStatus("error", error instanceof IpcCallError ? error : new IpcCallError("invalid_response", error instanceof Error ? error.message : "The mod rejected app registration."));
        throw error;
      });
      this.registration = { connection, promise };
    }
    await this.registration.promise;
  }

  private request(name: string, input: unknown, output: ValueSchema): Promise<unknown> {
    this.check();
    if (this.pending.size >= 256) return Promise.reject(new IpcCallError("busy", "Too many pending IPC calls."));
    const id = uuid();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id); reject(new IpcCallError("timeout", "The IPC call timed out. Its side effects may already have occurred."));
      }, this.timeoutMs);
      this.pending.set(id, { resolve, reject, output, timer });
      try { this.raw.send(name, input, { id, signal: this.signal }); }
      catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }

  private receive(value: unknown, message: IpcMessage, failed: boolean): void {
    if (!message.correlationId) return;
    const pending = this.pending.get(message.correlationId);
    if (!pending) return;
    this.pending.delete(message.correlationId); clearTimeout(pending.timer);
    if (failed) {
      const error = value as { code?: unknown; message?: unknown } | null;
      pending.reject(new IpcCallError(typeof error?.code === "string" ? error.code : "invalid_response",
        typeof error?.message === "string" ? error.message : "Invalid IPC error response."));
      return;
    }
    try { pending.resolve(decodeValue(pending.output, value, this.contract.models)); }
    catch (error) { pending.reject(error); }
  }

  private async handleApp(name: string, method: MethodDefinition, payload: unknown, message: IpcMessage): Promise<void> {
    const connectionId = this.connection.connectionId;
    const epoch = this.epoch;
    let id = message.id;
    let code = "invalid_request";
    try {
      this.check();
      const envelope = payload as { callId?: unknown; sessionId?: unknown; args?: unknown } | null;
      if (!envelope || typeof envelope.callId !== "string" || !envelope.callId || envelope.callId.length > 128
        || typeof envelope.sessionId !== "string" || !envelope.sessionId) throw new TypeError("Invalid app call envelope.");
      id = envelope.callId;
      // A synchronous transport may deliver the first push while rpc.register is still sending.
      // Wait only for that existing registration; an incoming callback never starts registration.
      await Promise.resolve();
      const registration = this.registration;
      if (!registration || registration.connection !== connectionId) return;
      await registration.promise;
      if (epoch !== this.epoch || connectionId !== this.connection.connectionId || envelope.sessionId !== this.sessionId) return;
      this.check();
      const request = decodeValue(method.input, envelope.args, this.contract.models);
      code = "handler_failed";
      const handler = this.handlers[name] as (input: unknown) => unknown;
      const result = await handler(request);
      if (epoch !== this.epoch || connectionId !== this.connection.connectionId) return;
      this.check(); code = "invalid_result";
      if (method.output === "void" && result !== undefined) throw new IpcValidationError("$", "A void handler must not return a value.");
      const value = decodeValue(method.output, method.output === "void" ? null : result, this.contract.models);
      this.raw.reply(id, "rpc.result", method.output === "void" ? null : value);
    } catch (error) {
      if (this.disposed || this.signal.aborted || epoch !== this.epoch || connectionId !== this.connection.connectionId) return;
      try { this.raw.reply(id, "rpc.error", { code, message: error instanceof Error ? error.message : "App handler failed." }); }
      catch { /* The connection may have closed while the handler was running. */ }
    }
  }
  private failAll(error: unknown): void {
    for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(error); }
    this.pending.clear();
  }
  private setStatus(status: ChannelStatus, error?: IpcCallError): void {
    if (this.status === status && this.statusError?.code === error?.code && this.statusError?.message === error?.message) return;
    this.status = status; this.statusError = error;
    for (const listener of [...this.statuses]) { try { listener(status, error); } catch { /* A consumer cannot break IPC dispatch. */ } }
  }
  dispose(): void {
    if (this.disposed) return;
    this.disposed = true; this.epoch++; this.sessionId = undefined;
    this.failAll(new IpcCallError("disposed", "The channel has been disposed."));
    this.setStatus("unavailable"); this.statuses.clear();
    for (const off of this.cleanup.splice(0)) off();
  }
}

export function createIpc(options: IpcOptions = {}): { registerApp(options: AppOptions): AppRegistration } {
  return { registerApp: app => new AppRegistration(app, options) };
}
export const ipc: ReturnType<typeof createIpc> = createIpc();
