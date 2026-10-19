import { createHash } from "node:crypto";
import { emitFile, isArrayModelType, isNullType, isVoidType } from "@typespec/compiler";
import ts from "typescript";
import { $lib, modKey, appKey } from "./index.js";

const sorted = map => [...map.values()].sort((a, b) => a.name.localeCompare(b.name, "en"));
const pascal = name => name[0].toUpperCase() + name.slice(1);
const quote = value => JSON.stringify(value).replace(/\u2028/g, "\\u2028").replace(/\u2029/g, "\\u2029");
const identifier = /^[a-zA-Z][a-zA-Z0-9]*$/;
const reserved = new Set(["ready", "onStatus", "status", "sessionId", "then", "constructor", "prototype"]);
const runtimeTypes = ["IpcContract", "IpcMethod", "IpcCodec", "IpcSchema", "IpcUnit", "IpcOptional", "IpcStringEnumConverter", "IpcApp", "IpcAppInvoker", "ModContract", "Task", "CancellationToken", "JsonProperty", "JsonPropertyAttribute", "JsonConverter", "EnumMember"];
const csKeywords = new Set("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while".split(" "));
const requestType = (name, method, role, mod) => {
  if (!Object.keys(method.input.object).length) return "IpcUnit";
  const suffix = role === "app" && mod[name] && Object.keys(mod[name].input.object).length &&
    JSON.stringify(mod[name].input) !== JSON.stringify(method.input) ? "AppRequest" : "Request";
  return `${pascal(name)}${suffix}`;
};

export function describeContract(program) {
  const roots = [...program.stateMap(modKey)];
  if (roots.length !== 1) throw new Error("Generate one @ipc.mod root per package; split different mod contracts into separate entrypoints.");
  const [root, id] = roots[0];
  if (!/^[A-Z][a-zA-Z0-9]*$/.test(root.name)) throw new Error("The mod namespace must have a PascalCase identifier.");
  const models = new Map(), enums = new Map(), visiting = new Set();
  function enumSchema(type, entries) {
    if (!entries.length || entries.some(([, value]) => typeof value !== "string")) throw new Error(`Enum ${type.name} must contain string values only.`);
    if (new Set(entries.map(([, value]) => value)).size !== entries.length) throw new Error(`Enum ${type.name} contains duplicate string values.`);
    if (!/^[A-Z][a-zA-Z0-9]*$/.test(type.name)) throw new Error(`Use a PascalCase enum name: ${type.name}.`);
    const existing = enums.get(type.name);
    if (existing && existing.type !== type) throw new Error(`Enum name collision: ${type.name}.`);
    const members = Object.fromEntries(entries.sort(([a], [b]) => a.localeCompare(b, "en")));
    enums.set(type.name, { type, members });
    return { enum: [...new Set(Object.values(members))].sort(), name: type.name };
  }
  function object(model) {
    if (model.indexer) throw new Error("Indexed models need an explicit dictionary contract.");
    const properties = new Map();
    const lineage = [];
    for (let current = model; current; current = current.baseModel) lineage.unshift(current);
    for (const current of lineage) for (const [key, property] of current.properties) properties.set(key, property);
    return { object: Object.fromEntries(sorted(properties).map(property => {
      if (!identifier.test(property.name) || property.name[0] !== property.name[0].toLowerCase()) throw new Error(`Use lowerCamelCase field names: ${property.name}.`);
      const schema = shape(property.type);
      return [property.name, property.optional ? { optional: schema } : schema];
    })) };
  }
  function stringValues(type) {
    if (type.kind === "String") return [type.value];
    if (type.kind === "EnumMember") {
      const value = type.value ?? type.name;
      if (typeof value === "string") return [value];
    }
    if (type.kind === "Union") {
      const values = [...type.variants.values()].map(variant => stringValues(variant.type));
      if (values.every(Boolean)) return values.flat();
    }
    return undefined;
  }
  function shape(type, allowVoid = false) {
    if (isVoidType(type)) {
      if (allowVoid) return "void";
      throw new Error("void is supported only as an operation return type.");
    }
    if (type.kind === "String" || type.kind === "EnumMember") {
      const values = stringValues(type);
      if (values) return { enum: values };
    }
    if (type.kind === "Enum") return enumSchema(type, [...type.members.values()].map(member => [member.name, member.value ?? member.name]));
    if (type.kind === "Union") {
      if (visiting.has(type)) throw new Error(`Recursive union ${type.name ?? "expression"} is not supported.`);
      visiting.add(type);
      try {
        const variants = [...type.variants.values()];
        const nonNull = variants.filter(variant => !isNullType(variant.type));
        const nullable = nonNull.length !== variants.length;
        let result;
        const values = nonNull.map(variant => stringValues(variant.type));
        if (nonNull.length && values.every(Boolean)) {
          const literals = [...new Set(values.flat())].sort();
          if (type.name) {
            const entries = nonNull.flatMap((variant, index) => values[index].map(value => [
              typeof variant.name === "string" && values[index].length === 1 ? variant.name : value, value,
            ]));
            result = enumSchema(type, entries);
          } else result = { enum: literals };
        } else if (nullable && nonNull.length === 1) result = shape(nonNull[0].type);
        else throw new Error(`Unsupported union ${type.name ?? "expression"}; use nullable values or string literals.`);
        return nullable ? { nullable: result } : result;
      } finally { visiting.delete(type); }
    }
    if (type.kind === "Scalar") {
      for (const name of ["string", "boolean", "int32", "float64", "safeint"]) if (type === program.checker.getStdType(name)) return name;
    }
    if (type.kind === "Model" && isArrayModelType(type)) return { array: shape(type.indexer.value) };
    if (type.kind === "Model" && type.name && !type.indexer) {
      if (!/^[A-Z][a-zA-Z0-9]*$/.test(type.name)) throw new Error(`Use a PascalCase model name: ${type.name}.`);
      if (visiting.has(type)) throw new Error(`Recursive model ${type.name} is not supported.`);
      const existing = models.get(type.name);
      if (existing && existing.type !== type) throw new Error(`Model name collision: ${type.name}.`);
      if (!existing) {
        visiting.add(type);
        const schema = object(type);
        models.set(type.name, { type, schema });
        visiting.delete(type);
      }
      return { model: type.name };
    }
    throw new Error(`Unsupported contract type ${type.name ?? type.kind}; use supported scalar, array, nullable, string enum or named model types.`);
  }
  function operations(list, role) {
    const result = {}, wires = new Set();
    for (const operation of [...list].sort((a, b) => a.name.localeCompare(b.name, "en"))) {
      if (!/^[a-z][a-zA-Z0-9]*$/.test(operation.name) || reserved.has(operation.name)) throw new Error(`Invalid or reserved operation name: ${operation.name}.`);
      const wireName = `rpc.${role}.${operation.name.toLowerCase()}`;
      if (wires.has(wireName) || Object.hasOwn(result, operation.name)) throw new Error(`Operation name collision: ${operation.name}.`);
      wires.add(wireName);
      result[operation.name] = { wireName, input: object(operation.parameters), output: shape(operation.returnType, true) };
    }
    return result;
  }
  const appOperations = [];
  for (const intf of program.stateSet(appKey)) {
    let parent = intf.namespace;
    while (parent && !program.stateMap(modKey).has(parent)) parent = parent.namespace;
    if (parent !== root) throw new Error(`App interface ${intf.name} must be inside its @ipc.mod root.`);
    appOperations.push(...intf.operations.values());
  }
  for (const intf of root.interfaces.values()) if (!program.stateSet(appKey).has(intf)) throw new Error(`Interface ${intf.name} needs @ipc.app; put mod operations directly on the root.`);
  const mod = operations(root.operations.values(), "mod"), app = operations(appOperations, "app");
  for (const model of sorted(root.models)) shape(model);
  for (const enumeration of sorted(root.enums)) shape(enumeration);
  for (const union of sorted(root.unions)) shape(union);
  const data = { id, models: Object.fromEntries([...models].sort(([a], [b]) => a.localeCompare(b, "en")).map(([name, model]) => [name, model.schema])), mod, app };
  const publicNames = new Set([...runtimeTypes, `${root.name}Contract`, `${root.name}Methods`, `${root.name}App`, `${root.name}AppMethods`, `${root.name}Schemas`, `${root.name}Mod`, `${root.name}AppHandlers`]);
  for (const name of [...Object.keys(data.models), ...enums.keys()]) {
    if (publicNames.has(name)) throw new Error(`Generated type name collision: ${name}.`);
    publicNames.add(name);
  }
  const requestShapes = new Map();
  for (const [role, methods] of [["mod", mod], ["app", app]]) for (const [name, method] of Object.entries(methods)) {
    if (!Object.keys(method.input.object).length) continue;
    const request = requestType(name, method, role, mod);
    if (requestShapes.has(request) && JSON.stringify(requestShapes.get(request)) === JSON.stringify(method.input)) continue;
    if (publicNames.has(request)) throw new Error(`Generated request type name collision: ${request}.`);
    publicNames.add(request); requestShapes.set(request, method.input);
  }
  for (const [name, schema] of [...Object.entries(data.models), ...requestShapes])
    if (Object.keys(schema.object).some(field => pascal(field) === name)) throw new Error(`A field collides with its generated C# class name: ${name}.`);
  return { name: root.name, ...data, enums: Object.fromEntries([...enums].sort(([a], [b]) => a.localeCompare(b, "en")).map(([name, enumeration]) => [name, enumeration.members])), hash: createHash("sha256").update(JSON.stringify(data)).digest("hex") };
}

function tsType(schema) {
  if (["int32", "float64", "safeint"].includes(schema)) return "number";
  if (typeof schema === "string") return schema;
  if (schema.model) return schema.model;
  if (schema.optional) return tsType(schema.optional);
  if (schema.nullable) return `${tsType(schema.nullable)} | null`;
  if (schema.array) return `Array<${tsType(schema.array)}>`;
  if (schema.enum) return schema.name ?? schema.enum.map(quote).join(" | ");
  return `{ ${Object.entries(schema.object).map(([name, type]) => `${name}${type.optional ? "?" : ""}: ${tsType(type)}`).join("; ")} }`;
}
function csType(schema) {
  if (schema === "int32") return "int";
  if (schema === "boolean") return "bool";
  if (schema === "string") return "string";
  if (schema === "float64") return "double";
  if (schema === "safeint") return "long";
  if (schema === "void") return "IpcUnit";
  if (schema.optional) return `IpcOptional<${csType(schema.optional)}>`;
  if (schema.nullable) return `${csType(schema.nullable)}?`;
  if (schema.array) return `${csType(schema.array)}[]`;
  if (schema.enum) return schema.name ?? "string";
  return schema.model;
}
function csSchema(schema, owner) {
  if (schema === "int32") return "IpcSchema.Int32";
  if (schema === "float64") return "IpcSchema.Float64";
  if (schema === "safeint") return "IpcSchema.SafeInt";
  if (schema === "void") return "IpcSchema.Null";
  if (typeof schema === "string") return `IpcSchema.${pascal(schema)}`;
  if (schema.model) return `${owner}Schemas.${schema.model}()`;
  for (const wrapper of ["array", "nullable", "optional"]) if (schema[wrapper]) return `IpcSchema.${pascal(wrapper)}(${csSchema(schema[wrapper], owner)})`;
  if (schema.enum) return schema.enum.length === 1 ? `IpcSchema.StringLiteral(${quote(schema.enum[0])})` : `IpcSchema.StringEnum(${schema.enum.map(quote).join(", ")})`;
  return `IpcSchema.Object(${Object.entries(schema.object).map(([key, type]) => `IpcSchema.Field(${quote(key)}, ${csSchema(type, owner)})`).join(", ")})`;
}
function enumMemberIdentifiers(name, members) {
  const used = new Set([name]);
  return Object.keys(members).map((member, index) => {
    let value = member.split(/[^A-Za-z0-9]+/).filter(Boolean).map(pascal).join("") || `Value${index + 1}`;
    if (!/^[A-Za-z]/.test(value)) value = "Value" + value;
    const original = value;
    let suffix = 2;
    while (used.has(value)) value = original + suffix++;
    used.add(value);
    return [member, csKeywords.has(value) ? "@" + value : value];
  });
}
export function renderContract(program, options = {}) {
  const contract = describeContract(program), name = contract.name;
  const packageName = options.packageName ?? `@generated/${contract.id}-ipc`;
  if (!/^(?:@[a-z0-9._-]+\/)?[a-z0-9._-]+$/.test(packageName)) throw new Error("Invalid generated package name.");
  let ns = options.csharpNamespace ?? `${name}.Generated`;
  if (!/^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$/.test(ns)) throw new Error("Invalid C# namespace.");
  ns = ns.split(".").map(part => csKeywords.has(part) ? "@" + part : part).join(".");
  const declarations = [
    'import { defineContract, type IpcContract } from "adofai-ipc";',
    ...Object.entries(contract.enums).flatMap(([enumeration, members]) => [
      `export const ${enumeration}: { ${Object.entries(members).map(([member, value]) => `readonly ${quote(member)}: ${quote(value)}`).join("; ")} } = Object.freeze({ ${Object.entries(members).map(([member, value]) => `[${quote(member)}]: ${quote(value)}`).join(", ")} } as const);`,
      `export type ${enumeration} = typeof ${enumeration}[keyof typeof ${enumeration}];`,
    ]),
    ...Object.entries(contract.models).map(([model, shape]) => `export interface ${model} ${tsType(shape)}`),
    `export interface ${name}Mod {\n${Object.entries(contract.mod).map(([op, method]) => `  ${quote(op)}(${Object.keys(method.input.object).length ? `request: ${tsType(method.input)}` : ""}): Promise<${tsType(method.output)}>;`).join("\n")}\n}`,
    `export interface ${name}AppHandlers {\n${Object.entries(contract.app).map(([op, method]) => `  ${quote(op)}(${Object.keys(method.input.object).length ? `request: ${tsType(method.input)}` : ""}): ${tsType(method.output)} | Promise<${tsType(method.output)}>;`).join("\n")}\n}`,
    `export const ${name}Contract: IpcContract<${name}Mod, ${name}AppHandlers> = defineContract<${name}Mod, ${name}AppHandlers>(${JSON.stringify({ id: contract.id, hash: contract.hash, models: contract.models, mod: contract.mod, app: contract.app }, null, 2)});`,
  ].join("\n\n") + "\n";
  const generated = "// Generated by @adofai-ipc/typespec. Do not edit.\n";
  const js = ts.transpileModule(declarations, { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.ESNext } }).outputText;
  const dts = ts.transpileDeclaration(declarations, { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.ESNext, isolatedDeclarations: true } });
  if (dts.diagnostics?.some(d => d.category === ts.DiagnosticCategory.Error)) throw new Error(dts.diagnostics.map(d => ts.flattenDiagnosticMessageText(d.messageText, " ")).join("\n"));
  function dto(type, schema) {
    return `public sealed class ${type}\n{\n${Object.entries(schema.object).map(([key, value]) => `  [JsonProperty(${quote(key)})] public ${csType(value)} ${pascal(key)} { get; set; } = default!;`).join("\n")}\n}`;
  }
  const requests = new Map();
  const request = (op, method, role = "mod") => requestType(op, method, role, contract.mod);
  for (const [role, methods] of [["mod", contract.mod], ["app", contract.app]])
    for (const [op, method] of Object.entries(methods)) if (Object.keys(method.input.object).length) requests.set(request(op, method, role), method.input);
  function methodFields(methods, role) {
    return Object.entries(methods).map(([op, method]) => `  public static readonly IpcMethod<${request(op, method, role)}, ${csType(method.output)}> ${pascal(op)} = new IpcMethod<${request(op, method, role)}, ${csType(method.output)}>(${quote(op)}, ${quote(method.wireName)}, new IpcCodec<${request(op, method, role)}>(${csSchema(method.input, name)}), new IpcCodec<${csType(method.output)}>(${csSchema(method.output, name)}));`).join("\n");
  }
  const csharp = generated + [
    "#nullable enable\nusing System.Runtime.Serialization;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing AdofaiIpc;\nusing Newtonsoft.Json;",
    `namespace ${ns};`,
    ...Object.entries(contract.enums).map(([enumeration, members]) => `[JsonConverter(typeof(IpcStringEnumConverter))]\npublic enum ${enumeration}\n{\n${enumMemberIdentifiers(enumeration, members).map(([member, identifier]) => `  [EnumMember(Value = ${quote(members[member])})] ${identifier},`).join("\n")}\n}`),
    ...Object.entries(contract.models).map(([name, schema]) => dto(name, schema)),
    ...[...requests].map(([type, schema]) => dto(type, schema)),
    `internal static class ${name}Schemas\n{\n${Object.entries(contract.models).map(([model, schema]) => `  internal static IpcSchema ${model}() => ${csSchema(schema, name)};`).join("\n")}\n}`,
    `public static class ${name}Methods\n{\n${methodFields(contract.mod, "mod")}\n}`,
    `internal static class ${name}AppMethods\n{\n${methodFields(contract.app, "app")}\n}`,
    `public sealed class ${name}App : IpcApp\n{\n  public ${name}App(IpcAppInvoker invoker) : base(invoker) { }\n${Object.entries(contract.app).map(([op, method]) => `  public ${method.output === "void" ? "Task" : `Task<${csType(method.output)}>`} ${pascal(op)}Async(${Object.keys(method.input.object).length ? `${request(op, method, "app")} request, ` : ""}CancellationToken cancellationToken = default) => ${method.output === "void" ? "InvokeVoidAsync" : "InvokeAsync"}(${name}AppMethods.${pascal(op)}, ${Object.keys(method.input.object).length ? "request" : "IpcUnit.Value"}, cancellationToken);`).join("\n")}\n}`,
    `public sealed class ${name}Contract : ModContract<${name}App>\n{\n  public static readonly ${name}Contract Instance = new ${name}Contract();\n  private ${name}Contract() : base(${quote(contract.id)}, ${quote(contract.hash)}, new IpcMethod[] { ${Object.keys(contract.mod).map(op => `${name}Methods.${pascal(op)}`).join(", ")} }, invoker => new ${name}App(invoker)) { }\n}`,
  ].join("\n\n") + "\n";
  const packageVersion = options.packageVersion ?? "0.0.0";
  if (typeof packageVersion !== "string" || !/^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(packageVersion)) throw new Error("Invalid generated package version.");
  if (options.packagePrivate !== undefined && typeof options.packagePrivate !== "boolean") throw new Error("Generated package private must be a boolean.");
  const runtimeDependency = options.runtimeDependency ?? (options.workspace ? "workspace:^" : "^2.0.0");
  if (typeof runtimeDependency !== "string" || !runtimeDependency.trim()) throw new Error("Generated runtimeDependency must be a non-empty package specifier.");
  const packageJson = JSON.stringify({ name: packageName, version: packageVersion, private: options.packagePrivate ?? true, type: "module", adofaiIpcGenerated: true,
    main: "./index.js", types: "./index.d.ts", exports: { ".": { types: "./index.d.ts", default: "./index.js" } },
    dependencies: { "adofai-ipc": runtimeDependency } }, null, 2) + "\n";
  return { contract, typescript: { "index.ts": generated + declarations, "index.js": generated + js, "index.d.ts": generated + dts.outputText, "package.json": packageJson }, csharp: { [`${name}.g.cs`]: csharp } };
}

export async function $onEmit(context) {
  try {
    const output = renderContract(context.program, context.options);
    for (const language of ["typescript", "csharp"]) for (const [file, content] of Object.entries(output[language]))
      await emitFile(context.program, { path: `${context.emitterOutputDir}/${language}/${file}`, content });
  } catch (error) {
    $lib.reportDiagnostic(context.program, { code: "contract", target: context.program.getGlobalNamespaceType(), format: { message: error.message } });
  }
}
