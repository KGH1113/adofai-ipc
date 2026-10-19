#!/usr/bin/env node
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { runCli } from "../dist/composition/cli.js";
export { generate } from "../dist/composition/cli.js";

const entry = process.argv[1] ? await fs.realpath(path.resolve(process.argv[1])).catch(() => undefined) : undefined;
if (entry === fileURLToPath(import.meta.url)) await runCli();
