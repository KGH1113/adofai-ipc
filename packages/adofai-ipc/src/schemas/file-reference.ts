import { IpcValidationError } from "../api/validation-error";

export interface FileReference { url: string; byteLength: number; fileName: string; contentType: string }
export function decodeFileReference(value: unknown, origin: string | undefined): FileReference {
  const fail = (): never => { throw new IpcValidationError("$", "Invalid file response from the connected IPC runtime."); };
  if (!value || typeof value !== "object" || Array.isArray(value)) return fail();
  const fields = ["url", "byteLength", "fileName", "contentType"];
  if (Object.keys(value).length !== fields.length) return fail();
  const copy: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
  for (const key of fields) {
    const property = Object.getOwnPropertyDescriptor(value, key);
    if (!property || !("value" in property)) return fail();
    copy[key] = property.value;
  }
  if (typeof copy.url !== "string" || typeof copy.fileName !== "string" || !copy.fileName.trim() || copy.fileName.length > 255
    || typeof copy.contentType !== "string" || !copy.contentType.trim() || copy.contentType.length > 256
    || typeof copy.byteLength !== "number" || !Number.isSafeInteger(copy.byteLength) || copy.byteLength < 0
    || /[\u0000-\u001f\u007f]/.test(copy.fileName + copy.contentType)) return fail();
  let url: URL;
  try { url = new URL(copy.url); } catch { return fail(); }
  if (!origin || url.origin !== origin || url.protocol !== "http:" || !["localhost", "127.0.0.1"].includes(url.hostname)
    || url.username || url.password || url.search || url.hash || !/^\/ipc\/download\/[A-Za-z0-9_-]{43}$/.test(url.pathname)) return fail();
  return copy as unknown as FileReference;
}
