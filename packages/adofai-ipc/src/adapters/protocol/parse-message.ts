import type { IpcMessage } from "../../models/messaging";
import { MAX_MESSAGE_BYTES } from "../../models/protocol";
const encoder = new TextEncoder();
export function parseMessage(text: string): IpcMessage | null {
  if (encoder.encode(text).length > MAX_MESSAGE_BYTES) return null;
  try { const value = JSON.parse(text) as IpcMessage; return value && typeof value.kind === "string" && typeof value.name === "string" && typeof value.id === "string" ? value : null; } catch { return null; }
}
