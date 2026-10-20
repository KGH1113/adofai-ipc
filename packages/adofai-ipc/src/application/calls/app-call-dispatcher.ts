import { IpcValidationError } from "../../api/validation-error";
import type { MethodDefinition } from "../../models/contract";
import type { IpcMessage } from "../../models/messaging";
import type { Models } from "../../models/value-schema";
import type { MessageChannel } from "../../ports/message-session";
import { decodeValue } from "../../schemas/decode-value";

export interface AppCallSession {
  check(): void;
  connectionId(): string | undefined;
  epoch(): number;
  id(): string | undefined;
  registration(): { connection: string; promise: Promise<void> } | undefined;
  disposed(): boolean;
}
export class AppCallDispatcher {
  constructor(private readonly raw: MessageChannel, private readonly models: Models,
    private readonly handlers: Record<string, unknown>, private readonly signal: AbortSignal,
    private readonly session: AppCallSession, private readonly file?: (value: unknown) => unknown) {}
  async handle(name: string, method: MethodDefinition, payload: unknown, message: IpcMessage): Promise<void> {
    const connectionId = this.session.connectionId();
    const epoch = this.session.epoch();
    let id = message.id;
    let code = "invalid_request";
    try {
      this.session.check();
      const envelope = payload as { callId?: unknown; sessionId?: unknown; args?: unknown } | null;
      if (!envelope || typeof envelope.callId !== "string" || !envelope.callId || envelope.callId.length > 128
        || typeof envelope.sessionId !== "string" || !envelope.sessionId) throw new TypeError("Invalid app call envelope.");
      id = envelope.callId;
      // A synchronous transport may deliver the first push while rpc.register is still sending.
      // Wait only for that existing registration; an incoming callback never starts registration.
      await Promise.resolve();
      const registration = this.session.registration();
      if (!registration || registration.connection !== connectionId) return;
      await registration.promise;
      if (epoch !== this.session.epoch() || connectionId !== this.session.connectionId() || envelope.sessionId !== this.session.id()) return;
      this.session.check();
      const request = decodeValue(method.input, envelope.args, this.models, "$", 0, this.file);
      code = "handler_failed";
      const handler = this.handlers[name] as (input: unknown) => unknown;
      const result = await handler(request);
      if (epoch !== this.session.epoch() || connectionId !== this.session.connectionId()) return;
      this.session.check(); code = "invalid_result";
      if (method.output === "void" && result !== undefined) throw new IpcValidationError("$", "A void handler must not return a value.");
      const value = decodeValue(method.output, method.output === "void" ? null : result, this.models);
      this.raw.reply(id, "rpc.result", method.output === "void" ? null : value);
    } catch (error) {
      if (this.session.disposed() || this.signal.aborted || epoch !== this.session.epoch() || connectionId !== this.session.connectionId()) return;
      try { this.raw.reply(id, "rpc.error", { code, message: error instanceof Error ? error.message : "App handler failed." }); }
      catch { /* The connection may have closed while the handler was running. */ }
    }
  }
}
