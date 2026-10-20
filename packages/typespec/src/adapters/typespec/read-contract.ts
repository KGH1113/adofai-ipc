import { createHash } from "node:crypto";
import { isArrayModelType, isNullType, isVoidType } from "@typespec/compiler";
import type { Program, Namespace, Model, ModelProperty, Type, Enum, Union, Operation, Interface } from "@typespec/compiler";
import type { Contract, Schema, ObjectSchema, EnumSchema, Methods, Role } from "../../models/contract.js";
import { sorted, pascal, identifier, reserved, runtimeTypes, requestType } from "../../models/names.js";
import { modKey, appKey } from "./decorators.js";
import { containsFile } from "../../models/contract.js";

export function describeContract(program: Program): Contract {
  const roots = [...program.stateMap(modKey) as Map<Namespace, string>];
  if (roots.length !== 1) throw new Error("Generate one @ipc.mod root per package; split different mod contracts into separate entrypoints.");
  const [root, id] = roots[0]!;
  if (!/^[A-Z][a-zA-Z0-9]*$/.test(root.name)) throw new Error("The mod namespace must have a PascalCase identifier.");
  const models = new Map<string, { type: Model; schema: ObjectSchema }>();
  const enums = new Map<string, { type: Enum | Union; members: Record<string, string> }>();
  const visiting = new Set<Type>();
  const fileType = program.getGlobalNamespaceType().namespaces.get("ipc")?.models.get("File");
  function derivesFile(model: Model, seen = new Set<Model>()): boolean {
    if (model === fileType) return true;
    if (seen.has(model)) return false;
    seen.add(model);
    return !!(model.baseModel && derivesFile(model.baseModel, seen)) || model.sourceModels.some(source => derivesFile(source.model, seen));
  }
  function enumSchema(type: Enum | Union, entries: [string, string | number][]): EnumSchema {
    if (!entries.length || entries.some(([, value]) => typeof value !== "string")) throw new Error(`Enum ${type.name} must contain string values only.`);
    if (new Set(entries.map(([, value]) => value)).size !== entries.length) throw new Error(`Enum ${type.name} contains duplicate string values.`);
    if (!type.name || !/^[A-Z][a-zA-Z0-9]*$/.test(type.name)) throw new Error(`Use a PascalCase enum name: ${type.name}.`);
    const existing = enums.get(type.name);
    if (existing && existing.type !== type) throw new Error(`Enum name collision: ${type.name}.`);
    const members = Object.fromEntries(entries.sort(([a], [b]) => a.localeCompare(b, "en"))) as Record<string, string>;
    enums.set(type.name, { type, members });
    return { enum: [...new Set(Object.values(members))].sort(), name: type.name };
  }
  function object(model: Model): ObjectSchema {
    if (model.indexer) throw new Error("Indexed models need an explicit dictionary contract.");
    const properties = new Map<string, ModelProperty>();
    const lineage: Model[] = [];
    for (let current: Model | undefined = model; current; current = current.baseModel) lineage.unshift(current);
    for (const current of lineage) for (const [key, property] of current.properties) properties.set(key, property);
    return { object: Object.fromEntries(sorted(properties).map(property => {
      if (!identifier.test(property.name) || property.name[0] !== property.name[0]!.toLowerCase()) throw new Error(`Use lowerCamelCase field names: ${property.name}.`);
      const schema = shape(property.type);
      return [property.name, property.optional ? { optional: schema } : schema];
    })) };
  }
  function stringValues(type: Type): string[] | undefined {
    if (type.kind === "String") return [type.value];
    if (type.kind === "EnumMember") {
      const value = type.value ?? type.name;
      if (typeof value === "string") return [value];
    }
    if (type.kind === "Union") {
      const values = [...type.variants.values()].map(variant => stringValues(variant.type));
      if (values.every((value): value is string[] => value !== undefined)) return values.flat();
    }
    return undefined;
  }
  function shape(type: Type, allowVoid = false): Schema {
    if (type === fileType) return "file";
    if (type.kind === "Model" && fileType && derivesFile(type)) throw new Error("Use ipc.File directly or as a field; extending or copying it is not supported.");
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
        let result: Schema;
        const values = nonNull.map(variant => stringValues(variant.type));
        if (nonNull.length && values.every((value): value is string[] => value !== undefined)) {
          const literals = [...new Set(values.flat())].sort();
          if (type.name) {
            const entries = nonNull.flatMap((variant, index) => values[index]!.map(value => [
              typeof variant.name === "string" && values[index]!.length === 1 ? variant.name : value, value,
            ] as [string, string]));
            result = enumSchema(type, entries);
          } else result = { enum: literals };
        } else if (nullable && nonNull.length === 1) result = shape(nonNull[0]!.type);
        else throw new Error(`Unsupported union ${type.name ?? "expression"}; use nullable values or string literals.`);
        return nullable ? { nullable: result } : result;
      } finally { visiting.delete(type); }
    }
    if (type.kind === "Scalar") {
      for (const name of ["string", "boolean", "int32", "float64", "safeint"] as const) if (type === program.checker.getStdType(name)) return name;
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
    throw new Error(`Unsupported contract type ${String(("name" in type ? type.name : undefined) ?? type.kind)}; use supported scalar, array, nullable, string enum or named model types.`);
  }
  function operations(list: Iterable<Operation>, role: Role): Methods {
    const result: Methods = {}, wires = new Set<string>();
    for (const operation of [...list].sort((a, b) => a.name.localeCompare(b.name, "en"))) {
      if (!/^[a-z][a-zA-Z0-9]*$/.test(operation.name) || reserved.has(operation.name)) throw new Error(`Invalid or reserved operation name: ${operation.name}.`);
      const wireName = `rpc.${role}.${operation.name.toLowerCase()}`;
      if (wires.has(wireName) || Object.hasOwn(result, operation.name)) throw new Error(`Operation name collision: ${operation.name}.`);
      wires.add(wireName);
      result[operation.name] = { wireName, input: object(operation.parameters), output: shape(operation.returnType, true) };
    }
    return result;
  }
  const appOperations: Operation[] = [];
  for (const intf of program.stateSet(appKey) as Set<Interface>) {
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
  for (const method of Object.values(mod)) if (containsFile(method.input, data.models))
    throw new Error("ipc.File is supported in mod results and app parameters; app-to-mod uploads are not supported.");
  for (const method of Object.values(app)) if (containsFile(method.output, data.models))
    throw new Error("ipc.File is supported in mod results and app parameters; app-to-mod uploads are not supported.");
  const publicNames = new Set([...runtimeTypes, `${root.name}Contract`, `${root.name}Methods`, `${root.name}App`, `${root.name}AppMethods`, `${root.name}Schemas`, `${root.name}Mod`, `${root.name}AppHandlers`]);
  for (const name of [...Object.keys(data.models), ...enums.keys()]) {
    if (publicNames.has(name)) throw new Error(`Generated type name collision: ${name}.`);
    publicNames.add(name);
  }
  const requestShapes = new Map<string, ObjectSchema>();
  for (const [role, methods] of [["mod", mod], ["app", app]] as const) for (const [name, method] of Object.entries(methods)) {
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
