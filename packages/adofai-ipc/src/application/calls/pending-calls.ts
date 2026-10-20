import { IpcCallError } from "../../api/errors";
import type { IpcMessage } from "../../models/messaging";
import type { Models, ValueSchema } from "../../models/value-schema";
import type { MessageChannel } from "../../ports/message-session";
import { decodeValue } from "../../schemas/decode-value";

interface Pending {
  resolve(value: unknown): void;
  reject(error: unknown): void;
  output: ValueSchema;
  timer: ReturnType<typeof setTimeout>;
}
/** Correlation, deadlines and cancellation for one registered channel. */
export class PendingCalls {
  private readonly pending = new Map<string, Pending>();
  constructor(private readonly check: () => void, private readonly raw: MessageChannel,
    private readonly models: Models, private readonly signal: AbortSignal,
    private readonly timeoutMs: number, private readonly createId: () => string, private readonly file?: (value: unknown) => unknown) {}
  request(name: string, input: unknown, output: ValueSchema): Promise<unknown> {
    this.check();
    if (this.pending.size >= 256) return Promise.reject(new IpcCallError("busy", "Too many pending IPC calls."));
    const id = this.createId();
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id); reject(new IpcCallError("timeout", "The IPC call timed out. Its side effects may already have occurred."));
      }, this.timeoutMs);
      this.pending.set(id, { resolve, reject, output, timer });
      try { this.raw.send(name, input, { id, signal: this.signal }); }
      catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }

  receive(value: unknown, message: IpcMessage, failed: boolean): void {
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
    try { pending.resolve(decodeValue(pending.output, value, this.models, "$", 0, this.file)); }
    catch (error) { pending.reject(error); }
  }

  failAll(error: unknown): void {
    for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(error); }
    this.pending.clear();
  }
}
