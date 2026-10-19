import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const root = fileURLToPath(new URL("../../../", import.meta.url));
const violations = [];
const innerLayers = new Set(["application", "models", "ports", "schemas"]);
const outerLayers = new Set(["adapters", "composition", "vendor"]);
const ignored = new Set(["node_modules", "bin", "obj", "dist", "Vendor"]);

function files(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (ignored.has(entry.name)) return [];
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? files(file) : [file];
  });
}

function layer(directory, file) {
  return path.relative(directory, file).split(path.sep)[0].toLowerCase();
}

function report(file, text, offset, reason) {
  const line = text.slice(0, offset).split("\n").length;
  violations.push(`${path.relative(root, file)}:${line}: ${reason}`);
}

function checkTypeScript(relative) {
  const directory = path.join(root, relative);
  const sources = files(directory).filter(file => file.endsWith(".ts"));
  for (const file of sources) {
    const text = fs.readFileSync(file, "utf8");
    const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true);
    const sourceLayer = layer(directory, file);
    function visit(node) {
      let specifier;
      if (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) specifier = node.moduleSpecifier;
      if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) specifier = node.arguments[0];
      if (specifier && ts.isStringLiteral(specifier)) {
        const name = specifier.text;
        const targetLayer = name.startsWith(".") ? layer(directory, path.resolve(path.dirname(file), name)) : undefined;
        if (innerLayers.has(sourceLayer) && outerLayers.has(targetLayer))
          report(file, text, node.getStart(), `${sourceLayer} cannot depend on ${targetLayer}: ${name}`);
        if (sourceLayer === "models" && targetLayer && targetLayer !== "models")
          report(file, text, node.getStart(), `models cannot depend on ${targetLayer}: ${name}`);
        if ((sourceLayer === "ports" || sourceLayer === "schemas") && targetLayer === "application")
          report(file, text, node.getStart(), `${sourceLayer} cannot depend on application: ${name}`);
        if (innerLayers.has(sourceLayer) && (name.startsWith("node:") || name === "@typespec/compiler"))
          report(file, text, node.getStart(), `${sourceLayer} cannot depend on an environment adapter: ${name}`);
        if (sourceLayer === "emitters" && (name.startsWith("node:") || name === "@typespec/compiler"))
          report(file, text, node.getStart(), `emitters must consume the common contract model: ${name}`);
      }
      ts.forEachChild(node, visit);
    }
    visit(source);
  }
  return sources.length;
}

function stripCommentsAndStrings(text) {
  return text.replace(/\/\*[\s\S]*?\*\/|\/\/[^\n]*|@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/g,
    value => value.replace(/[^\n]/g, " "));
}

function checkCSharp(relative) {
  const directory = path.join(root, relative);
  const sources = files(directory).filter(file => file.endsWith(".cs"));
  const owners = new Map();
  const bodies = new Map();
  for (const file of sources) {
    const body = stripCommentsAndStrings(fs.readFileSync(file, "utf8"));
    bodies.set(file, body);
    for (const match of body.matchAll(/\b(?:class|interface|struct|enum)\s+(\w+)/g)) {
      const layers = owners.get(match[1]) ?? new Set();
      layers.add(layer(directory, file));
      owners.set(match[1], layers);
    }
  }
  for (const file of sources) {
    const sourceLayer = layer(directory, file);
    if (!innerLayers.has(sourceLayer)) continue;
    const body = bodies.get(file);
    for (const match of body.matchAll(/\b[A-Za-z_]\w*\b/g)) {
      const layers = owners.get(match[0]);
      if (layers && [...layers].every(value => outerLayers.has(value)))
        report(file, body, match.index, `${sourceLayer} cannot depend on adapter implementation ${match[0]}`);
      if (sourceLayer === "models" && layers && !layers.has("models"))
        report(file, body, match.index, `models cannot depend on ${match[0]}`);
      if ((sourceLayer === "ports" || sourceLayer === "schemas") && layers?.has("application"))
        report(file, body, match.index, `${sourceLayer} cannot depend on application type ${match[0]}`);
    }
    for (const match of body.matchAll(/\b(?:Newtonsoft\.Json|UnityEngine|System\.Reflection|JsonConvert|JToken|JObject|JArray)\b/g))
      report(file, body, match.index, `${sourceLayer} must use a port instead of ${match[0]}`);
    for (const match of body.matchAll(/\b(?:File|Directory)\.(?:Read\w*|Write\w*|Open\w*|Exists|GetFiles|GetDirectories|Create\w*|Delete|Move|Enumerate\w*)\b/g))
      report(file, body, match.index, `${sourceLayer} must use a file-system port instead of ${match[0]}`);
  }
  return sources.length;
}

let count = checkTypeScript("packages/adofai-ipc/src") + checkTypeScript("packages/typespec/src");
count += checkCSharp("AdofaiIpc/src/AdofaiIpc.Loader") + checkCSharp("AdofaiIpc/src/AdofaiIpc.Runtime");
for (const old of ["packages/client/package.json", "packages/docs/package.json", "AdofaiIpc.sln"])
  if (fs.existsSync(path.join(root, old))) violations.push(`Obsolete source location remains: ${old}`);
const generator = files(path.join(root, "packages/typespec/src"));
if (generator.some(file => file.endsWith(".js"))) violations.push("TypeSpec generator source must be TypeScript.");

if (violations.length) {
  console.error([...new Set(violations)].join("\n"));
  process.exitCode = 1;
} else console.log(`Validated architecture boundaries across ${count} source files.`);
