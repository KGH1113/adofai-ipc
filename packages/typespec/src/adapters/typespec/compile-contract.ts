import { compile, NodeHost, formatDiagnostic } from "@typespec/compiler";
import type { ContractReader } from "../../ports/generation.js";
import { describeContract } from "./read-contract.js";

export const typeSpecReader: ContractReader = {
  async read(entry) {
    const program = await compile(NodeHost, entry, { noEmit: true });
    if (program.hasError()) throw new Error(program.diagnostics.map(d => formatDiagnostic(d)).join("\n"));
    return describeContract(program);
  },
};
