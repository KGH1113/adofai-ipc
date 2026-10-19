import type { Program } from "@typespec/compiler";
import type { GenerationOptions } from "../models/contract.js";
import { describeContract } from "../adapters/typespec/read-contract.js";
import { emitTypeScript } from "../emitters/typescript/emit-package.js";
import { emitCSharp } from "../emitters/csharp/emit-source.js";
import { generateContract } from "../application/generate-contract.js";

export { describeContract };
export const emitters = { typescript: emitTypeScript, csharp: emitCSharp };
export function renderContract(program: Program, options: GenerationOptions = {}) {
  return generateContract(describeContract(program), options, emitters);
}
