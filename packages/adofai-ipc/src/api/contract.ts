import type { ContractDefinition, IpcContract } from "../models/contract";

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
