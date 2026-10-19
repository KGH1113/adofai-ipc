import type { Models, ValueSchema } from "./value-schema";

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
