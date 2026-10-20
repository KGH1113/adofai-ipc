import type { AppOptions } from "../../api/options";
import type { Channel } from "../../api/channel";
import type { IpcContract } from "../../models/contract";
import type { MessageSession } from "../../ports/message-session";
import { IpcCallError } from "../../api/errors";
import { ContractBinding } from "../sessions/contract-binding";
import type { FileReader } from "../../ports/file-reader";
import type { FileDownloader } from "../../ports/file-downloader";

export class RegisteredApp {
  readonly appId: string;
  readonly displayName: string;

  private readonly bindings = new Map<string, ContractBinding>();
  private readonly lifetime = new AbortController();
  private readonly timeoutMs: number;
  private readonly offState: () => void;
  constructor(options: AppOptions, private readonly connection: MessageSession, appId: string, private readonly createId: () => string, requestTimeoutMs = 30000,
    private readonly fileReader?: FileReader, private readonly fileDownloader?: FileDownloader) {
    if (!options || typeof options.displayName !== "string" || !options.displayName.trim() || options.displayName.length > 200)
      throw new TypeError("A displayName of 1–200 characters is required.");
    this.displayName = options.displayName;
    this.appId = appId;
    this.timeoutMs = requestTimeoutMs;
    if (!Number.isFinite(this.timeoutMs) || this.timeoutMs <= 0) throw new TypeError("Invalid IPC request timeout.");
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
    const binding = new ContractBinding(this, this.connection, contract, implementations, this.lifetime.signal, this.timeoutMs, this.createId, this.fileReader, this.fileDownloader);
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
