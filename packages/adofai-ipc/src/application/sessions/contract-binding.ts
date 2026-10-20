import { AppCallDispatcher } from "../calls/app-call-dispatcher";
import { PendingCalls } from "../calls/pending-calls";
import type { ChannelStatus } from "../../api/channel";
import { IpcCallError } from "../../api/errors";
import type { ContractDefinition } from "../../models/contract";
import type { ValueSchema } from "../../models/value-schema";
import type { MessageChannel, MessageSession } from "../../ports/message-session";
import { decodeValue } from "../../schemas/decode-value";
import { IncomingFiles } from "../files/incoming-files";
import type { FileReader } from "../../ports/file-reader";
import type { FileDownloader } from "../../ports/file-downloader";

const registrationOutput: ValueSchema = { object: { appId: "string", sessionId: "string" } };

export class ContractBinding {
  readonly api: object;
  private readonly raw: MessageChannel;
  private readonly calls: PendingCalls;
  private readonly callbacks: AppCallDispatcher;
  private readonly cleanup: Array<() => void> = [];
  private registration: { connection: string; promise: Promise<void> } | undefined;
  private readonly statuses = new Set<(status: ChannelStatus, error?: IpcCallError) => void>();
  private status: ChannelStatus = "unavailable";
  private statusError: IpcCallError | undefined;
  private sessionId: string | undefined;
  private epoch = 0;
  private disposed = false;
  private readonly files: IncomingFiles;
  constructor(private readonly app: { appId: string; displayName: string }, private readonly connection: MessageSession,
    private readonly contract: ContractDefinition, handlers: Record<string, unknown>,
    private readonly signal: AbortSignal, timeoutMs: number, createId: () => string, fileReader?: FileReader, fileDownloader?: FileDownloader) {
    this.files = new IncomingFiles(fileReader, () => connection.downloadOrigin, fileDownloader);
    this.raw = connection.namespace(contract.id, 1);
    this.calls = new PendingCalls(() => this.check(), this.raw, contract.models, signal, timeoutMs, createId, this.files.decode);
    this.callbacks = new AppCallDispatcher(this.raw, contract.models, handlers, signal, {
      check: () => this.check(), connectionId: () => connection.connectionId, epoch: () => this.epoch,
      id: () => this.sessionId, registration: () => this.registration, disposed: () => this.disposed,
    }, this.files.decode);
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
        return this.calls.request(method.wireName, request, method.output);
      };
    }
    this.api = Object.freeze(api);
    this.cleanup.push(this.raw.on("rpc.result", (value, message) => this.calls.receive(value, message, false)));
    for (const name of ["rpc.error", "command.rejected", "command.failed"]) {
      this.cleanup.push(this.raw.on(name, (value, message) => this.calls.receive(value, message, true)));
    }
    for (const [name, method] of Object.entries(contract.app)) {
      this.cleanup.push(this.raw.onCommand(method.wireName, (payload, message) => { void this.callbacks.handle(name, method, payload, message); }));
    }
    this.cleanup.push(this.raw.onStatus((status, error) => {
      if (status === "ready") {
        // Register without waiting for a first mod call so the mod can push its initial snapshot.
        void this.ready().catch(() => {});
      } else {
        this.epoch++; this.sessionId = undefined; this.registration = undefined;
        const reason = new IpcCallError(error?.code ?? "unavailable", error?.message ?? "The mod is no longer available.");
        this.files.invalidate(reason);
        this.calls.failAll(reason); this.setStatus(status, status === "error" ? reason : undefined);
      }
    }));
    const abort = () => {
      this.files.invalidate(signal.reason);
      this.epoch++; this.sessionId = undefined;
      this.calls.failAll(signal.reason);
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
      const promise = this.calls.request("rpc.register", {
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

  private setStatus(status: ChannelStatus, error?: IpcCallError): void {
    if (this.status === status && this.statusError?.code === error?.code && this.statusError?.message === error?.message) return;
    this.status = status; this.statusError = error;
    for (const listener of [...this.statuses]) { try { listener(status, error); } catch { /* A consumer cannot break IPC dispatch. */ } }
  }
  dispose(): void {
    if (this.disposed) return;
    this.disposed = true; this.epoch++; this.sessionId = undefined;
    this.files.invalidate();
    this.calls.failAll(new IpcCallError("disposed", "The channel has been disposed."));
    this.setStatus("unavailable"); this.statuses.clear();
    for (const off of this.cleanup.splice(0)) off();
  }
}
