import fs from "node:fs";
import path from "node:path";
import assert from "node:assert/strict";

const root = process.argv[2];
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const manifest = JSON.parse(read("AdofaiIpc/manifest.json"));
const version = manifest.RuntimeVersion;
assert.match(version, /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/);
assert.equal(manifest.SchemaVersion, 1);
assert.equal(manifest.ContractMajor, 1);
assert.equal(manifest.ContractAssemblyVersion, "1.0.0.0");
assert.equal(manifest.WireMajor, 3);
for (const file of ["packages/client/package.json", "packages/adofai-ipc/package.json", "packages/typespec/package.json"])
  assert.equal(JSON.parse(read(file)).version, version, file);
assert.equal(read("Directory.Build.props").match(/<ProductVersion>(.*?)<\/ProductVersion>/)[1], version);
assert.equal(read("AdofaiIpc/IpcProtocol.cs").match(/ProductVersion = "(.*?)"/)[1], version);
assert.equal(read("packages/client/src/version.ts").match(/CLIENT_VERSION = "(.*?)"/)[1], version);
const contract = read("AdofaiIpc.Contracts/IpcContracts.cs");
assert.equal(Number(contract.match(/const int Major = (\d+)/)[1]), manifest.ContractMajor);
assert.equal(Number(contract.match(/const int WireMajor = (\d+)/)[1]), manifest.WireMajor);
assert.equal(contract.match(/const string AssemblyVersion = "(.*?)"/)[1], manifest.ContractAssemblyVersion);
assert.equal(Number(read("AdofaiIpc/IpcProtocol.cs").match(/const int Version = (\d+)/)[1]), manifest.WireMajor);
assert.equal(Number(read("packages/client/src/version.ts").match(/PROTOCOL_VERSION = (\d+)/)[1]), manifest.WireMajor);
for (const file of ["AdofaiIpc.Contracts/AdofaiIpc.Contracts.csproj", "AdofaiIpc.Loader/AdofaiIpc.Loader.csproj"])
  assert.equal(read(file).match(/<AssemblyVersion>(.*?)<\/AssemblyVersion>/)[1], "1.0.0.0");
for (const tag of [process.env.GITHUB_REF_NAME, process.env.RELEASE_TAG].filter(Boolean))
  assert.equal(tag.replace(/^v/, ""), version);
console.log(`Validated runtime ${version}, contract ABI 1.0.0.0, wire 3.`);
