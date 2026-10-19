import { generateProject } from "../application/generate-project.js";
import { readConfig } from "../adapters/filesystem/read-config.js";
import { fileOutputWriter } from "../adapters/filesystem/write-output.js";
import { typeSpecReader } from "../adapters/typespec/compile-contract.js";
import { emitters } from "./render-contract.js";

export async function generate(configFile: string, check = false) {
  return generateProject(await readConfig(configFile), check, typeSpecReader, fileOutputWriter, emitters);
}
export async function runCli(argsInput = process.argv.slice(2)): Promise<void> {
  try {
    const args = argsInput;
    if (args[0] !== "generate" || args.includes("--help")) {
      console.log("Usage: adofai-ipc generate [--config adofai-ipc.config.json] [--check]");
      if (args[0] !== "generate" && args.length && !args.includes("--help")) process.exitCode = 1;
    } else {
      const index = args.indexOf("--config");
      if (index !== -1 && !args[index + 1]) throw new Error("--config requires a path.");
      const known = args.filter((_, i) => i !== 0 && i !== index && i !== index + 1);
      if (known.some(value => value !== "--check")) throw new Error("Unknown generator option.");
      const result = await generate(index === -1 ? "adofai-ipc.config.json" : args[index + 1]!, args.includes("--check"));
      console.log(`IPC ${result.id}: ${result.changes.length} file(s) ${args.includes("--check") ? "need updating" : "updated"}.`);
    }
  } catch (error) { console.error(error instanceof Error ? error.message : String(error)); process.exitCode = 1; }
}
