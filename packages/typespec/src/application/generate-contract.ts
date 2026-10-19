import type { Contract, GeneratedContract, GenerationOptions } from "../models/contract.js";
import type { TargetEmitters } from "../ports/generation.js";

export function generateContract(contract: Contract, options: GenerationOptions, emitters: TargetEmitters): GeneratedContract {
  return { contract, typescript: emitters.typescript(contract, options), csharp: emitters.csharp(contract, options) };
}
