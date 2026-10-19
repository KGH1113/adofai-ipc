import type { Schema } from "../../models/contract.js";
import { pascal, quote, csKeywords } from "../../models/names.js";

export function csType(schema: Schema): string {
  if (schema === "int32") return "int";
  if (schema === "boolean") return "bool";
  if (schema === "string") return "string";
  if (schema === "float64") return "double";
  if (schema === "safeint") return "long";
  if (schema === "void") return "IpcUnit";
  if ("optional" in schema) return `IpcOptional<${csType(schema.optional)}>`;
  if ("nullable" in schema) return `${csType(schema.nullable)}?`;
  if ("array" in schema) return `${csType(schema.array)}[]`;
  if ("enum" in schema) return schema.name ?? "string";
  if ("model" in schema) return schema.model;
  throw new Error("Anonymous object types must be named before emitting C#.");
}
export function csSchema(schema: Schema, owner: string): string {
  if (schema === "int32") return "IpcSchema.Int32";
  if (schema === "float64") return "IpcSchema.Float64";
  if (schema === "safeint") return "IpcSchema.SafeInt";
  if (schema === "void") return "IpcSchema.Null";
  if (typeof schema === "string") return `IpcSchema.${pascal(schema)}`;
  if ("model" in schema) return `${owner}Schemas.${schema.model}()`;
  if ("array" in schema) return `IpcSchema.Array(${csSchema(schema.array, owner)})`;
  if ("nullable" in schema) return `IpcSchema.Nullable(${csSchema(schema.nullable, owner)})`;
  if ("optional" in schema) return `IpcSchema.Optional(${csSchema(schema.optional, owner)})`;
  if ("enum" in schema) return schema.enum.length === 1 ? `IpcSchema.StringLiteral(${quote(schema.enum[0]!)})` : `IpcSchema.StringEnum(${schema.enum.map(quote).join(", ")})`;
  return `IpcSchema.Object(${Object.entries(schema.object).map(([key, type]) => `IpcSchema.Field(${quote(key)}, ${csSchema(type, owner)})`).join(", ")})`;
}
export function enumMemberIdentifiers(name: string, members: Record<string, string>): [string, string][] {
  const used = new Set([name]);
  return Object.keys(members).map((member, index) => {
    let value = member.split(/[^A-Za-z0-9]+/).filter(Boolean).map(pascal).join("") || `Value${index + 1}`;
    if (!/^[A-Za-z]/.test(value)) value = "Value" + value;
    const original = value;
    let suffix = 2;
    while (used.has(value)) value = original + suffix++;
    used.add(value);
    return [member, csKeywords.has(value) ? "@" + value : value] as [string, string];
  });
}
