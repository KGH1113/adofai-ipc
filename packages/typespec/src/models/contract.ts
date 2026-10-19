export type PrimitiveSchema = "string" | "boolean" | "int32" | "float64" | "safeint" | "void";
export interface ObjectSchema { object: Record<string, Schema> }
export interface EnumSchema { enum: string[]; name?: string }
export type Schema = PrimitiveSchema | ObjectSchema | EnumSchema | { model: string }
  | { array: Schema } | { nullable: Schema } | { optional: Schema };
export interface Method { wireName: string; input: ObjectSchema; output: Schema }
export type Methods = Record<string, Method>;
export type Role = "mod" | "app";
export interface Contract {
  name: string;
  id: string;
  hash: string;
  models: Record<string, ObjectSchema>;
  enums: Record<string, Record<string, string>>;
  mod: Methods;
  app: Methods;
}
export interface GenerationOptions {
  packageName?: string;
  csharpNamespace?: string;
  workspace?: boolean;
  packageVersion?: string;
  packagePrivate?: boolean;
  runtimeDependency?: string;
}
export type Language = "typescript" | "csharp";
export type SourceFiles = Record<string, string>;
export interface GeneratedContract {
  contract: Contract;
  typescript: SourceFiles;
  csharp: SourceFiles;
}
