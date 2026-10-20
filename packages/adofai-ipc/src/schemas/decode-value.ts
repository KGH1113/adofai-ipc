import type { Models, ValueSchema } from "../models/value-schema";
import { IpcValidationError } from "../api/validation-error";

/** Validate and copy values so getters, prototypes and toJSON cannot change the wire data. */
export function decodeValue(schema: ValueSchema, value: unknown, models: Models, path = "$", depth = 0, file?: (value: unknown) => unknown): unknown {
  if (depth > 32) throw new IpcValidationError(path, "Maximum data depth exceeded.");
  if (schema === "file") {
    if (!file) throw new IpcValidationError(path, "Files may only be received from a mod.");
    return file(value);
  }
  if (schema === "string" || schema === "boolean") {
    if (typeof value !== schema) throw new IpcValidationError(path, `Expected ${schema}.`);
    return value;
  }
  if (schema === "int32") {
    if (typeof value !== "number" || !Number.isInteger(value) || value < -2147483648 || value > 2147483647)
      throw new IpcValidationError(path, "Expected a signed 32-bit integer.");
    return value;
  }
  if (schema === "float64" || schema === "safeint") {
    if (typeof value !== "number" || !Number.isFinite(value) || (schema === "safeint" && !Number.isSafeInteger(value)))
      throw new IpcValidationError(path, schema === "safeint" ? "Expected an integer within the JavaScript safe integer range." : "Expected a finite 64-bit floating point number.");
    return value;
  }
  if (schema === "void") {
    if (value !== null) throw new IpcValidationError(path, "Expected a null wire value for a void result.");
    return undefined;
  }
  if ("nullable" in schema) return value === null ? null : decodeValue(schema.nullable, value, models, path, depth + 1, file);
  if ("optional" in schema) return value === undefined ? undefined : decodeValue(schema.optional, value, models, path, depth + 1, file);
  if ("enum" in schema) {
    if (typeof value !== "string" || !schema.enum.includes(value))
      throw new IpcValidationError(path, `Expected one of: ${schema.enum.map(item => JSON.stringify(item)).join(", ")}.`);
    return value;
  }
  if ("array" in schema) {
    if (!Array.isArray(value)) throw new IpcValidationError(path, "Expected an array.");
    for (const key of Object.keys(value)) {
      if (!/^(0|[1-9][0-9]*)$/.test(key) || Number(key) >= value.length)
        throw new IpcValidationError(`${path}.${key}`, "Unknown array field.");
    }
    const result: unknown[] = [];
    for (let index = 0; index < value.length; index++) {
      const property = Object.getOwnPropertyDescriptor(value, index);
      if (!property || !("value" in property)) throw new IpcValidationError(`${path}[${index}]`, "An array element is required.");
      result.push(decodeValue(schema.array, property.value, models, `${path}[${index}]`, depth + 1, file));
    }
    return result;
  }
  if ("model" in schema) {
    const model = Object.hasOwn(models, schema.model) ? models[schema.model] : undefined;
    if (!model) throw new IpcValidationError(path, `Unknown model ${schema.model}.`);
    return decodeValue(model, value, models, path, depth + 1, file);
  }
  if (value === null || typeof value !== "object" || Array.isArray(value)) throw new IpcValidationError(path, "Expected an object.");
  const fields = schema.object;
  for (const key of Object.keys(value)) {
    if (!Object.hasOwn(fields, key)) throw new IpcValidationError(`${path}.${key}`, "Unknown field.");
  }
  const result: Record<string, unknown> = Object.create(null) as Record<string, unknown>;
  for (const [key, field] of Object.entries(fields)) {
    const property = Object.getOwnPropertyDescriptor(value, key);
    const optional = typeof field === "object" && "optional" in field;
    if (!property && optional) continue;
    if (!property || !("value" in property)) throw new IpcValidationError(`${path}.${key}`, "A data field is required.");
    if (optional && property.value === undefined) continue;
    result[key] = decodeValue(field, property.value, models, `${path}.${key}`, depth + 1, file);
  }
  return result;
}
