// Spec 0014 GNR-1: the parts of an OpenAPI 3.1 document the generator reads. Spectral (CLT-1) proves the
// document valid, so this only narrows what it needs and fails loudly on what it can't use.
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

/** A JSON Schema as the server's transformers emit it (OpenAPI 3.1). */
export interface SchemaObject {
  $ref?: string;
  type?: string | string[];
  format?: string;
  description?: string;
  enum?: unknown[];
  items?: SchemaObject;
  properties?: Record<string, SchemaObject>;
  required?: string[];
  additionalProperties?: boolean | SchemaObject;
  oneOf?: SchemaObject[];
  anyOf?: SchemaObject[];
  allOf?: SchemaObject[];
  readOnly?: boolean;
  deprecated?: boolean;
  maximum?: number;
  pattern?: string;
}

export interface ParameterObject {
  $ref?: string;
  name?: string;
  in?: "path" | "query" | "header" | "cookie";
  description?: string;
  required?: boolean;
  deprecated?: boolean;
  schema?: SchemaObject;
}

export interface MediaTypeObject {
  schema?: SchemaObject;
}

export interface ResponseObject {
  description?: string;
  content?: Record<string, MediaTypeObject>;
}

export interface OperationObject {
  operationId?: string;
  summary?: string;
  description?: string;
  tags?: string[];
  deprecated?: boolean;
  parameters?: ParameterObject[];
  requestBody?: {
    description?: string;
    required?: boolean;
    content?: Record<string, MediaTypeObject>;
  };
  responses?: Record<string, ResponseObject>;
  "x-orvano-sdk"?: { service?: string; method?: string };
  "x-orvano-audience"?: string[];
  "x-orvano-errors"?: string[];
  "x-orvano-paging"?: { total?: boolean };
  "x-orvano-concurrency"?: boolean;
  "x-orvano-idempotent"?: boolean;
  "x-orvano-metered"?: string;
  "x-orvano-sdk-template"?: string;
}

export type PathItem = Partial<Record<HttpMethodKey, OperationObject>> & {
  parameters?: ParameterObject[];
};

export type HttpMethodKey = "get" | "put" | "post" | "delete" | "patch" | "head" | "options";

export const httpMethodKeys: readonly HttpMethodKey[] = [
  "delete",
  "get",
  "head",
  "options",
  "patch",
  "post",
  "put",
];

export interface ErrorCodeObject {
  status: number;
  description: string;
  members?: string[];
}

export interface OpenApiDocument {
  openapi: string;
  info: { title: string; version: string; description?: string };
  tags?: { name: string; description?: string }[];
  paths?: Record<string, PathItem>;
  components?: {
    schemas?: Record<string, SchemaObject>;
    parameters?: Record<string, ParameterObject>;
    headers?: Record<string, { description?: string }>;
  };
  "x-orvano-error-codes"?: Record<string, ErrorCodeObject>;
}

/** A failure the person running the generator must fix in the contract. */
export class GeneratorError extends Error {
  override readonly name = "GeneratorError";
}

/** The contract, its bytes, and their hash (GNR-8, RGN-1). */
export interface LoadedContract {
  document: OpenApiDocument;
  sha256: string;
}

export function loadContract(path: string): LoadedContract {
  return parseContract(readFileSync(path));
}

export function parseContract(bytes: Uint8Array): LoadedContract {
  const sha256 = createHash("sha256").update(bytes).digest("hex");
  const parsed = JSON.parse(new TextDecoder().decode(bytes)) as Partial<OpenApiDocument>;
  if (typeof parsed.openapi !== "string" || !parsed.openapi.startsWith("3.1")) {
    throw new GeneratorError("The contract is not an OpenAPI 3.1 document.");
  }
  if (typeof parsed.info?.version !== "string") {
    throw new GeneratorError("The contract has no info.version.");
  }
  return { document: parsed as OpenApiDocument, sha256 };
}

const schemaRef = /^#\/components\/schemas\/([^/]+)$/;
const parameterRef = /^#\/components\/parameters\/([^/]+)$/;

/** The schema name a `$ref` points to, or a GeneratorError naming where it was used. */
export function schemaRefName(document: OpenApiDocument, ref: string, where: string): string {
  const match = schemaRef.exec(ref);
  const name = match?.[1];
  if (name === undefined || document.components?.schemas?.[name] === undefined) {
    throw new GeneratorError(`${where}: unresolved reference ${ref}.`);
  }
  return name;
}

export function resolveParameter(
  document: OpenApiDocument,
  parameter: ParameterObject,
  where: string,
): ParameterObject {
  if (parameter.$ref === undefined) return parameter;
  const name = parameterRef.exec(parameter.$ref)?.[1];
  const resolved = name === undefined ? undefined : document.components?.parameters?.[name];
  if (resolved === undefined)
    throw new GeneratorError(`${where}: unresolved reference ${parameter.$ref}.`);
  return resolved;
}
