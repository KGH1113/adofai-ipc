import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import { build } from "tsup";

const root = path.resolve(fileURLToPath(new URL("../../..", import.meta.url)));
const destination = process.argv[2] && path.resolve(process.argv[2]);
if (!destination || destination === path.parse(destination).root || destination === root) throw new Error("Specify a vendor SDK directory.");
const contains = (parent, child) => {
  const relative = path.relative(parent, child);
  return relative === "" || (!relative.startsWith(".." + path.sep) && relative !== ".." && !path.isAbsolute(relative));
};
if (contains(destination, root) || ["packages/adofai-ipc", "packages/client", "scripts", ".git"]
  .some(directory => contains(path.join(root, directory), destination)))
  throw new Error("The vendor destination must not replace IPC source files.");
const sdk = JSON.parse(await fs.readFile(path.join(root, "packages/adofai-ipc/package.json"), "utf8"));
try {
  const contents = await fs.readdir(destination);
  if (contents.length) {
    let owned = false;
    try { owned = JSON.parse(await fs.readFile(path.join(destination, "source.json"), "utf8")).source === "https://github.com/KGH1113/adofai-ipc"; } catch {}
    try { owned ||= JSON.parse(await fs.readFile(path.join(destination, "package.json"), "utf8")).name === "adofai-ipc"; } catch {}
    if (!owned) throw new Error("Refusing to replace a directory that is not an ADOFAI IPC vendor package.");
  }
  await fs.rm(destination, { recursive: true });
} catch (error) { if (error.code !== "ENOENT") throw error; }
await build({ entry: [path.join(root, "packages/adofai-ipc/src/index.ts")], outDir: destination,
  tsconfig: path.join(root, "packages/adofai-ipc/tsconfig.json"), noExternal: ["@adofai-ipc/client"],
  format: ["esm"], platform: "browser", target: "es2020",
  dts: { resolve: true, compilerOptions: { rootDir: path.join(root, "packages/adofai-ipc/src") } }, clean: true });
await fs.copyFile(path.join(root, "LICENSE"), path.join(destination, "LICENSE"));
await fs.writeFile(path.join(destination, "package.json"), JSON.stringify({ name: "adofai-ipc", version: sdk.version,
  private: true, type: "module", main: "./index.js", types: "./index.d.ts",
  exports: { ".": { types: "./index.d.ts", default: "./index.js" } }, license: "MIT" }, null, 2) + "\n");
const files = {};
for (const name of (await fs.readdir(destination)).sort()) {
  if (name === "source.json") continue;
  files[name] = createHash("sha256").update(await fs.readFile(path.join(destination, name))).digest("hex");
}
const sourceRevision = execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
const sourceDirty = execFileSync("git", ["status", "--porcelain"], { cwd: root, encoding: "utf8" }).trim().length > 0;
await fs.writeFile(path.join(destination, "source.json"), JSON.stringify({ source: "https://github.com/KGH1113/adofai-ipc",
  sourceRevision, sourceDirty, version: sdk.version, protocolVersion: 3, files }, null, 2) + "\n");
console.log(`Exported standalone adofai-ipc ${sdk.version} to ${destination}`);
