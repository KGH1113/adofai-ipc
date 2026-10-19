import fs from "node:fs/promises";
import path from "node:path";
import type { GenerationRequest } from "../../application/generate-project.js";

interface Config {
  contract: string;
  typescript: { output: string; packageName?: string; workspace?: boolean; version?: string; private?: boolean; runtimeDependency?: string };
  csharp: { output: string; namespace?: string };
}
export async function readConfig(file: string): Promise<GenerationRequest> {
  const configPath = path.resolve(file), base = path.dirname(configPath);
  const config = JSON.parse(await fs.readFile(configPath, "utf8")) as Config;
  if (typeof config.contract !== "string" || typeof config.typescript?.output !== "string" || typeof config.csharp?.output !== "string")
    throw new Error("Set contract, typescript.output and csharp.output in the IPC generator config.");
  return { entry: path.resolve(base, config.contract),
    targets: { typescript: path.resolve(base, config.typescript.output), csharp: path.resolve(base, config.csharp.output) },
    options: { packageName: config.typescript.packageName, csharpNamespace: config.csharp.namespace, workspace: config.typescript.workspace,
      packageVersion: config.typescript.version, packagePrivate: config.typescript.private, runtimeDependency: config.typescript.runtimeDependency } };
}
