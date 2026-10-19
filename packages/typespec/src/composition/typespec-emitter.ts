import { emitFile } from "@typespec/compiler";
import type { EmitContext } from "@typespec/compiler";
import type { GenerationOptions } from "../models/contract.js";
import { $lib } from "../adapters/typespec/decorators.js";
import { renderContract } from "./render-contract.js";

export async function $onEmit(context: EmitContext<GenerationOptions>) {
  try {
    const output = renderContract(context.program, context.options);
    for (const language of ["typescript", "csharp"] as const)
      for (const [file, content] of Object.entries(output[language]))
        await emitFile(context.program, { path: `${context.emitterOutputDir}/${language}/${file}`, content });
  } catch (error) {
    $lib.reportDiagnostic(context.program, { code: "contract", target: context.program.getGlobalNamespaceType(),
      format: { message: error instanceof Error ? error.message : String(error) } });
  }
}
