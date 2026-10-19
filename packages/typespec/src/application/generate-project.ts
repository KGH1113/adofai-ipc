import type { GenerationOptions, Language } from "../models/contract.js";
import type { ContractReader, OutputWriter, TargetEmitters } from "../ports/generation.js";
import { generateContract } from "./generate-contract.js";

export interface GenerationRequest { entry: string; targets: Record<Language, string>; options: GenerationOptions }
export async function generateProject(request: GenerationRequest, check: boolean,
  reader: ContractReader, writer: OutputWriter, emitters: TargetEmitters) {
  const contract = await reader.read(request.entry);
  const output = generateContract(contract, request.options, emitters);
  const changes = await writer.sync(output, request.targets, check);
  return { id: contract.id, hash: contract.hash, changes };
}
