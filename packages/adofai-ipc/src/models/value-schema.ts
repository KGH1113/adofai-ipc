export type ValueSchema = "string" | "boolean" | "int32" | "float64" | "safeint" | "void" | "file"
  | { readonly model: string }
  | { readonly object: Readonly<Record<string, ValueSchema>> }
  | { readonly array: ValueSchema }
  | { readonly nullable: ValueSchema }
  | { readonly optional: ValueSchema }
  | { readonly enum: readonly string[]; readonly name?: string };
export type Models = Readonly<Record<string, ValueSchema>>;
