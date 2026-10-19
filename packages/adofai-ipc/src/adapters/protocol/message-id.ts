let nextId = 0;
export function messageId(): string { return globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(36)}-${(++nextId).toString(36)}-${Math.random().toString(36).slice(2)}`; }
