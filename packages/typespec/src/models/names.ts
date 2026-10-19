import type { Method, Methods, Role } from "./contract.js";

export const sorted = <T extends { name?: string }>(map: { values(): IterableIterator<T> }): T[] => [...map.values()].sort((a, b) => (a.name ?? "").localeCompare(b.name ?? "", "en"));
export const pascal = (name: string): string => name[0]!.toUpperCase() + name.slice(1);
export const quote = (value: unknown): string => JSON.stringify(value).replace(/\u2028/g, "\\u2028").replace(/\u2029/g, "\\u2029");
export const identifier = /^[a-zA-Z][a-zA-Z0-9]*$/;
export const reserved = new Set(["ready", "onStatus", "status", "sessionId", "then", "constructor", "prototype"]);
export const runtimeTypes = ["IpcContract", "IpcMethod", "IpcCodec", "IpcSchema", "IpcUnit", "IpcOptional", "IpcStringEnumConverter", "IpcApp", "IpcAppInvoker", "ModContract", "Task", "CancellationToken", "JsonProperty", "JsonPropertyAttribute", "JsonConverter", "EnumMember"];
export const csKeywords = new Set("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while".split(" "));
export const requestType = (name: string, method: Method, role: Role, mod: Methods): string => {
  if (!Object.keys(method.input.object).length) return "IpcUnit";
  const suffix = role === "app" && mod[name] && Object.keys(mod[name].input.object).length &&
    JSON.stringify(mod[name].input) !== JSON.stringify(method.input) ? "AppRequest" : "Request";
  return `${pascal(name)}${suffix}`;
};
