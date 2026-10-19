import type { Contract, GeneratedContract, GenerationOptions, Language, SourceFiles } from "../models/contract.js";
export type TargetEmitter = (contract: Contract, options: GenerationOptions) => SourceFiles;
export interface TargetEmitters { typescript: TargetEmitter; csharp: TargetEmitter }
export interface ContractReader { read(entry: string): Promise<Contract> }
export interface OutputWriter {
  sync(output: GeneratedContract, targets: Record<Language, string>, check: boolean): Promise<string[]>;
}
