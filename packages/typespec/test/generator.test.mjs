import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { spawnSync } from "node:child_process";
import { compile, NodeHost, formatDiagnostic } from "@typespec/compiler";
import { renderContract } from "../src/generate.js";
import { generate } from "../bin/adofai-ipc.mjs";

const directory = fileURLToPath(new URL(".", import.meta.url));
const library = fileURLToPath(new URL("../lib/main.tsp", import.meta.url));
async function source(t, body) {
  const temporary = await fs.mkdtemp(path.join(directory, ".tmp-"));
  t.after(() => fs.rm(temporary, { recursive: true, force: true }));
  const file = path.join(temporary, "main.tsp");
  await fs.writeFile(file, `import ${JSON.stringify(library)};\n${body}`);
  const program = await compile(NodeHost, file, { noEmit: true });
  return { temporary, file, program };
}
function valid(program) { assert.equal(program.hasError(), false, program.diagnostics.map(formatDiagnostic).join("\n")); }

test("TypeSpec generates deterministic TS declarations and C# methods in both directions", async t => {
  const { program } = await source(t, `@ipc.mod("demo") namespace Demo {
    model State { value: int32; label: string; enabled: boolean; }
    op read(): State;
    op write(state: State): State;
    @ipc.app interface Browser { confirm(message: string): boolean; }
  }`);
  valid(program);
  const first = renderContract(program), second = renderContract(program);
  assert.deepEqual(first, second);
  assert.match(first.typescript["index.d.ts"], /"read"\(\): Promise<State>/);
  assert.match(first.typescript["index.d.ts"], /value: number/);
  assert.match(first.typescript["index.d.ts"], /"confirm"\(request:/);
  assert.match(first.csharp["Demo.g.cs"], /Task<bool> ConfirmAsync\(ConfirmRequest request, CancellationToken cancellationToken = default\)/);
  assert.match(first.csharp["Demo.g.cs"], /public int Value/);
  assert.match(first.csharp["Demo.g.cs"], /\[JsonProperty\("label"\)\]/);
  assert.equal(first.contract.app.confirm.wireName, "rpc.app.confirm");
});

test("contract hashes ignore declaration order and output package names", async t => {
  const one = await source(t, `@ipc.mod("demo") namespace Demo { op add(a: int32, b: int32): int32; op text(): string; }`);
  const two = await source(t, `@ipc.mod("demo") namespace Demo { op text(): string; op add(b: int32, a: int32): int32; }`);
  valid(one.program); valid(two.program);
  assert.equal(renderContract(one.program).contract.hash, renderContract(two.program, { packageName: "@other/output" }).contract.hash);
  const changed = await source(t, `@ipc.mod("demo") namespace Demo { op add(a: int32, b: string): int32; op text(): string; }`);
  assert.notEqual(renderContract(one.program).contract.hash, renderContract(changed.program).contract.hash);
});

for (const [name, body, error] of [
  ["missing mod", "namespace Demo { op text(): string; }", /one @ipc.mod/],
  ["ambiguous mod roots", '@ipc.mod("one") namespace One {} @ipc.mod("two") namespace Two {}', /one @ipc.mod/],
  ["unmarked interface", '@ipc.mod("demo") namespace Demo { interface Browser { read(): string; } }', /needs @ipc.app/],
  ["outside app", '@ipc.mod("demo") namespace Demo {} @ipc.app interface Browser { read(): string; }', /inside its @ipc.mod/],
  ["64-bit scalar", '@ipc.mod("demo") namespace Demo { op read(): int64; }', /Unsupported contract type/],
  ["recursive models", '@ipc.mod("demo") namespace Demo { model State { next: State; } op read(): State; }', /Recursive model/],
  ["numeric enum", '@ipc.mod("demo") namespace Demo { enum Phase { One: 1 } op read(): Phase; }', /string values only/],
  ["duplicate enum wire values", '@ipc.mod("demo") namespace Demo { enum Phase { One: "same", Two: "same" } op read(): Phase; }', /duplicate string values/],
  ["model union", '@ipc.mod("demo") namespace Demo { model One { one: string; } model Two { two: string; } op read(): One | Two; }', /Unsupported union/],
  ["dictionary model", '@ipc.mod("demo") namespace Demo { op read(): Record<string>; }', /Unsupported contract type/],
  ["reserved operation", '@ipc.mod("demo") namespace Demo { op ready(): string; }', /reserved operation/],
  ["reserved status member", '@ipc.mod("demo") namespace Demo { op status(): string; }', /reserved operation/],
  ["wire name collision", '@ipc.mod("demo") namespace Demo { op getURL(): string; op getUrl(): string; }', /name collision/],
  ["runtime type collision", '@ipc.mod("demo") namespace Demo { model IpcUnit { value: string; } op read(): IpcUnit; }', /type name collision/],
  ["generated field collision", '@ipc.mod("demo") namespace Demo { model State { state: string; } op read(): State; }', /class name/],
]) test(`generator rejects ${name}`, async t => {
  const { program } = await source(t, body);
  valid(program);
  assert.throws(() => renderContract(program), error);
});

test("the same method name in both directions generates distinct request types when needed", async t => {
  const { program } = await source(t, `@ipc.mod("demo") namespace Demo {
    op echo(value: string): string;
    @ipc.app interface Browser { echo(count: int32): int32; }
  }`);
  valid(program);
  const output = renderContract(program, { csharpNamespace: "class.Generated" }).csharp["Demo.g.cs"];
  assert.match(output, /namespace @class.Generated;/);
  assert.match(output, /class EchoRequest/);
  assert.match(output, /class EchoAppRequest/);
  assert.match(output, /EchoAsync\(EchoAppRequest request, CancellationToken/);
});

test("extended contract types preserve optional, nullable, arrays, numeric ranges and void", async t => {
  const { program } = await source(t, `@ipc.mod("demo") namespace Demo {
    enum Phase { Pending: "pending", Done: "done" }
    union State { Active: "active", Paused: "paused" }
    model Value {
      values: (int32 | null)[];
      nested: string[][];
      note: string | null;
      omitted?: string;
      optionalNull?: string | null;
      optionalCount?: safeint | null;
      ratio: float64;
      count: safeint;
      phase: Phase;
      state: State;
      literal: "fixed";
      literalUnion: "a" | "b";
    }
    op echo(value: Value): Value;
    op finish(): void;
    @ipc.app interface Browser { updated(value: Value): void; }
  }`);
  valid(program);
  const output = renderContract(program);
  assert.deepEqual(output.contract.models.Value.object.omitted, { optional: "string" });
  assert.deepEqual(output.contract.models.Value.object.optionalNull, { optional: { nullable: "string" } });
  assert.deepEqual(output.contract.models.Value.object.values, { array: { nullable: "int32" } });
  assert.deepEqual(output.contract.models.Value.object.literal, { enum: ["fixed"] });
  assert.deepEqual(output.contract.models.Value.object.phase, { enum: ["done", "pending"], name: "Phase" });
  assert.equal(output.contract.mod.finish.output, "void");
  assert.match(output.typescript["index.d.ts"], /omitted\?: string/);
  assert.match(output.typescript["index.d.ts"], /optionalNull\?: string \| null/);
  assert.match(output.typescript["index.d.ts"], /values: Array<number \| null>/);
  assert.match(output.typescript["index.d.ts"], /export declare const Phase:/);
  assert.match(output.typescript["index.d.ts"], /export type Phase = typeof Phase\[keyof typeof Phase\]/);
  assert.match(output.typescript["index.d.ts"], /"finish"\(\): Promise<void>/);
  assert.match(output.csharp["Demo.g.cs"], /IpcOptional<string> Omitted/);
  assert.match(output.csharp["Demo.g.cs"], /IpcOptional<string\?> OptionalNull/);
  assert.match(output.csharp["Demo.g.cs"], /IpcOptional<long\?> OptionalCount/);
  assert.match(output.csharp["Demo.g.cs"], /int\?\[\] Values/);
  assert.match(output.csharp["Demo.g.cs"], /public double Ratio/);
  assert.match(output.csharp["Demo.g.cs"], /public long Count/);
  assert.match(output.csharp["Demo.g.cs"], /JsonConverter\(typeof\(IpcStringEnumConverter\)\)/);
  assert.match(output.csharp["Demo.g.cs"], /EnumMember\(Value = "pending"\)/);
  assert.match(output.csharp["Demo.g.cs"], /IpcMethod<IpcUnit, IpcUnit> Finish/);
  assert.match(output.csharp["Demo.g.cs"], /Task UpdatedAsync\(UpdatedRequest request, CancellationToken cancellationToken = default\) => InvokeVoidAsync/);
});

test("enum constants retain wire values and unusual member names without invalid C# identifiers", async t => {
  const { program } = await source(t, `@ipc.mod("demo") namespace Demo {
    enum Phase { \`not-ready\`: "waiting", \`not_ready\`: "paused", Phase: "phase", \`__proto__\`: "literal", \`1\`: "one" }
    op read(): Phase;
  }`);
  valid(program);
  const output = renderContract(program);
  const file = path.join((await source(t, '@ipc.mod("unused") namespace Unused {}')).temporary, "generated.mjs");
  await fs.writeFile(file, output.typescript["index.js"]);
  const { Phase } = await import(file);
  assert.equal(Phase["not-ready"], "waiting");
  assert.equal(Phase.__proto__, "literal");
  assert.equal(Phase["1"], "one");
  assert.equal(Object.isFrozen(Phase), true);
  const cs = output.csharp["Demo.g.cs"];
  assert.match(cs, /NotReady,/);
  assert.match(cs, /NotReady2,/);
  assert.match(cs, /Phase2,/);
  assert.match(cs, /Value1,/);
});

test("nullable enum unions and nullable return types retain their named constants", async t => {
  const { program } = await source(t, `@ipc.mod("demo") namespace Demo {
    union Phase { Pending: "pending", Done: "done", null }
    op read(): Phase;
    op maybe(): float64 | null;
    op all(): (string | null)[] | null;
  }`);
  valid(program);
  const output = renderContract(program);
  assert.deepEqual(output.contract.mod.read.output, { nullable: { enum: ["done", "pending"], name: "Phase" } });
  assert.match(output.typescript["index.d.ts"], /Promise<Phase \| null>/);
  assert.match(output.csharp["Demo.g.cs"], /IpcMethod<IpcUnit, Phase\?> Read/);
  assert.match(output.csharp["Demo.g.cs"], /IpcMethod<IpcUnit, string\?\[\]\?> All/);
});

test("generated vendor package metadata is configurable without workspace dependencies", async t => {
  const { program } = await source(t, '@ipc.mod("demo") namespace Demo { op read(): string; }');
  valid(program);
  const output = renderContract(program, { packageVersion: "1.2.3", packagePrivate: false, runtimeDependency: "file:../adofai-ipc" });
  const pkg = JSON.parse(output.typescript["package.json"]);
  assert.equal(pkg.version, "1.2.3"); assert.equal(pkg.private, false);
  assert.deepEqual(pkg.dependencies, { "adofai-ipc": "file:../adofai-ipc" });
});

test("the standard TypeSpec emitter produces both language outputs", async t => {
  const { temporary, file } = await source(t, '@ipc.mod("demo") namespace Demo { op read(): string; }');
  const outputDir = path.join(temporary, "output");
  const program = await compile(NodeHost, file, { emit: [fileURLToPath(new URL("../src/index.js", import.meta.url))], outputDir });
  valid(program);
  const files = await fs.readdir(outputDir, { recursive: true });
  assert.ok(files.some(file => file.endsWith("typescript/index.js")));
  assert.ok(files.some(file => file.endsWith("csharp/Demo.g.cs")));
});

test("decorator validates mod IDs using TypeSpec diagnostics", async t => {
  const { program } = await source(t, '@ipc.mod("Invalid-ID") namespace Demo {}');
  assert.equal(program.hasError(), true);
  assert.ok(program.diagnostics.some(diagnostic => diagnostic.code === "@adofai-ipc/typespec/contract"));
});

test("CLI checks stale output, regenerates owned files and preserves handwritten sources", async t => {
  const { temporary, file, program } = await source(t, '@ipc.mod("demo") namespace Demo { op read(): string; }');
  valid(program);
  const config = path.join(temporary, "adofai-ipc.config.json");
  await fs.writeFile(config, JSON.stringify({ contract: "main.tsp", typescript: { output: "ts" }, csharp: { output: "cs" } }));
  await generate(config);
  assert.deepEqual((await generate(config, true)).changes, []);
  const handwritten = path.join(temporary, "cs", "Manual.cs");
  await fs.writeFile(handwritten, "// manual");
  await fs.appendFile(path.join(temporary, "ts", "index.js"), "// drift\n");
  await assert.rejects(generate(config, true), /out of date/);
  await generate(config);
  const original = await fs.readFile(file, "utf8");
  await fs.writeFile(file, original.replace("namespace Demo", "namespace Renamed"));
  await generate(config);
  await assert.rejects(fs.access(path.join(temporary, "cs", "Demo.g.cs")), { code: "ENOENT" });
  assert.match(await fs.readFile(path.join(temporary, "cs", "Renamed.g.cs"), "utf8"), /class RenamedContract/);
  assert.equal(await fs.readFile(handwritten, "utf8"), "// manual");
  await fs.writeFile(path.join(temporary, "ts", "package.json"), '{"name":"manual"}');
  await assert.rejects(generate(config), /non-generated package/);
});

test("checked-in interop output matches its TypeSpec contract", async () => {
  assert.deepEqual((await generate(path.resolve(directory, "fixtures/typed/adofai-ipc.config.json"), true)).changes, []);
});

test("the CLI executes when invoked through a node_modules-style symlink", async t => {
  const { temporary } = await source(t, '@ipc.mod("demo") namespace Demo { op read(): string; }');
  const config = path.join(temporary, "adofai-ipc.config.json");
  await fs.writeFile(config, JSON.stringify({ contract: "main.tsp", typescript: { output: "ts" }, csharp: { output: "cs" } }));
  const realCli = fileURLToPath(new URL("../bin/adofai-ipc.mjs", import.meta.url));
  const linkedDirectory = path.join(temporary, "linked-bin");
  await fs.symlink(path.dirname(realCli), linkedDirectory, "junction");
  const linked = path.join(linkedDirectory, path.basename(realCli));
  for (const args of [["generate", "--config", config], ["generate", "--config", config, "--check"], ["--help"]]) {
    const child = spawnSync(process.execPath, [linked, ...args], { encoding: "utf8" });
    assert.equal(child.status, 0, child.stderr);
    assert.match(child.stdout, args[0] === "--help" ? /Usage:/ : /IPC demo:.*file/);
  }
  await fs.access(path.join(temporary, "ts", "index.js"));
});
