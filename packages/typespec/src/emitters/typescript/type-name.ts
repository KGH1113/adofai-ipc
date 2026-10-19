import type { Schema } from "../../models/contract.js";
import { quote } from "../../models/names.js";

export function tsType(schema: Schema): string {
  if (typeof schema === "string") return ["int32", "float64", "safeint"].includes(schema) ? "number" : schema;
  if ("model" in schema) return schema.model;
  if ("optional" in schema) return tsType(schema.optional);
  if ("nullable" in schema) return `${tsType(schema.nullable)} | null`;
  if ("array" in schema) return `Array<${tsType(schema.array)}>`;
  if ("enum" in schema) return schema.name ?? schema.enum.map(quote).join(" | ");
  return `{ ${Object.entries(schema.object).map(([name, type]) => `${name}${typeof type !== "string" && "optional" in type ? "?" : ""}: ${tsType(type)}`).join("; ")} }`;
}
