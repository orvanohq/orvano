// Spec 0014 GNR-2: the one intermediate model every target renders. buildModel reads the contract once and fails
// with a message naming the operation for anything a target could not render (GNR-10).
import {
  GeneratorError,
  httpMethodKeys,
  resolveParameter,
  schemaRefName,
  type LoadedContract,
  type OpenApiDocument,
  type OperationObject,
  type ParameterObject,
  type SchemaObject,
} from "./contract.ts";
import { byString } from "./naming.ts";

export type Audience = "client" | "server";

export type TypeRef =
  | { kind: "string"; format: string | undefined }
  | { kind: "integer"; format: string | undefined }
  | { kind: "number" }
  | { kind: "boolean" }
  | { kind: "unknown" }
  | { kind: "null" }
  | { kind: "ref"; name: string }
  | { kind: "array"; items: TypeRef }
  | { kind: "map"; values: TypeRef }
  | { kind: "enum"; values: string[] }
  | { kind: "object"; properties: Property[] }
  | { kind: "union"; members: TypeRef[] }
  | { kind: "nullable"; inner: TypeRef };

export interface Property {
  name: string;
  description: string;
  required: boolean;
  readOnly: boolean;
  deprecated: boolean;
  type: TypeRef;
}

export interface NamedSchema {
  name: string;
  description: string;
  type: TypeRef;
}

export interface Parameter {
  name: string;
  location: "path" | "query";
  description: string;
  required: boolean;
  deprecated: boolean;
  type: TypeRef;
}

export type PathSegment =
  | { kind: "literal"; value: string }
  | { kind: "project" }
  | { kind: "param"; name: string };

export type HttpMethod = "DELETE" | "GET" | "HEAD" | "OPTIONS" | "PATCH" | "POST" | "PUT";

export interface RequestBody {
  /** The named request schema, when the body is a `$ref`. */
  schema: string | undefined;
  description: string;
  required: boolean;
  properties: Property[];
}

export interface Operation {
  operationId: string;
  service: string;
  method: string;
  httpMethod: HttpMethod;
  path: string;
  segments: PathSegment[];
  summary: string;
  description: string;
  deprecated: boolean;
  audience: Audience[];
  pathParams: Parameter[];
  queryParams: Parameter[];
  body: RequestBody | undefined;
  /** The first 2xx status the operation answers. */
  successStatus: number;
  /** The success body's type; undefined when it has none (204). */
  response: TypeRef | undefined;
  errors: string[];
  /** A paged list (PAG-6): `response` is the page schema, `item` the type of one of its items. */
  paging: Paging | undefined;
  concurrency: boolean;
  idempotent: boolean;
  metered: string | undefined;
  template: string | undefined;
}

export interface Paging {
  total: boolean;
  /** The page schema's name, such as `PageOfRowResponse`. */
  page: string;
  item: TypeRef;
}

export interface Service {
  name: string;
  description: string;
  operations: Operation[];
}

export interface ErrorCode {
  code: string;
  status: number;
  description: string;
  members: string[];
}

export interface HeaderName {
  name: string;
  description: string;
}

export interface Model {
  title: string;
  version: string;
  contractSha256: string;
  services: Service[];
  schemas: NamedSchema[];
  errorCodes: ErrorCode[];
  headers: HeaderName[];
}

const sdkName = /^[a-z][A-Za-z0-9]*$/;

export function buildModel(contract: LoadedContract): Model {
  const { document } = contract;
  const catalogue = document["x-orvano-error-codes"] ?? {};
  const operations: Operation[] = [];
  const seen = new Map<string, string>();

  for (const path of Object.keys(document.paths ?? {}).toSorted(byString)) {
    const item = document.paths?.[path] ?? {};
    for (const key of httpMethodKeys) {
      const raw = item[key];
      if (raw === undefined) continue;
      const operation = buildOperation(
        document,
        path,
        key.toUpperCase() as HttpMethod,
        raw,
        item.parameters ?? [],
      );
      for (const code of operation.errors) {
        if (catalogue[code] === undefined) {
          throw new GeneratorError(
            `${operation.operationId}: x-orvano-errors lists ${code}, which is not in x-orvano-error-codes.`,
          );
        }
      }
      const pair = `${operation.service}.${operation.method}`;
      const other = seen.get(pair);
      if (other !== undefined) {
        throw new GeneratorError(
          `${operation.operationId}: ${pair} is also the SDK name of ${other}; each operation needs its own method.`,
        );
      }
      seen.set(pair, operation.operationId);
      operations.push(operation);
    }
  }

  // PAG-6: a paged list's walk all sibling takes the name `<method>All`, so no other operation may.
  for (const operation of operations) {
    if (operation.paging === undefined) continue;
    const pair = `${operation.service}.${operation.method}All`;
    const other = seen.get(pair);
    if (other !== undefined) {
      throw new GeneratorError(
        `${other}: ${pair} is the walk all method of ${operation.operationId}; name the operation something else.`,
      );
    }
  }

  const services = new Map<string, Operation[]>();
  for (const operation of operations) {
    services.set(operation.service, [...(services.get(operation.service) ?? []), operation]);
  }

  return {
    title: document.info.title,
    version: document.info.version,
    contractSha256: contract.sha256,
    services: [...services.keys()].toSorted(byString).map((name) => {
      const serviceOperations = (services.get(name) ?? []).toSorted((a, b) =>
        byString(a.method, b.method),
      );
      return {
        name,
        description: serviceDescription(document, name, serviceOperations),
        operations: serviceOperations,
      };
    }),
    schemas: Object.keys(document.components?.schemas ?? {})
      .toSorted(byString)
      .map((name) => {
        const schema = document.components?.schemas?.[name] ?? {};
        return {
          name,
          description: schema.description ?? "",
          type: toType(document, schema, `Schema ${name}`),
        };
      }),
    errorCodes: Object.keys(catalogue)
      .toSorted(byString)
      .map((code) => {
        const entry = catalogue[code];
        return {
          code,
          status: entry?.status ?? 0,
          description: entry?.description ?? "",
          members: [...(entry?.members ?? [])].toSorted(byString),
        };
      }),
    headers: headerNames(document),
  };
}

function buildOperation(
  document: OpenApiDocument,
  path: string,
  httpMethod: HttpMethod,
  raw: OperationObject,
  pathItemParameters: ParameterObject[],
): Operation {
  const operationId = raw.operationId;
  if (operationId === undefined || operationId === "") {
    throw new GeneratorError(`${httpMethod} ${path}: the operation has no operationId.`);
  }
  const sdk = raw["x-orvano-sdk"];
  if (sdk?.service === undefined || sdk.method === undefined) {
    throw new GeneratorError(
      `${operationId}: the operation has no x-orvano-sdk { service, method }.`,
    );
  }
  if (!sdkName.test(sdk.service) || !sdkName.test(sdk.method)) {
    throw new GeneratorError(
      `${operationId}: x-orvano-sdk names must be camelCase, got ${sdk.service}.${sdk.method}.`,
    );
  }
  const audience = (raw["x-orvano-audience"] ?? []).filter(
    (value): value is Audience => value === "client" || value === "server",
  );
  if (audience.length === 0) {
    throw new GeneratorError(`${operationId}: the operation has no x-orvano-audience.`);
  }

  const parameters = [...pathItemParameters, ...(raw.parameters ?? [])].map((parameter) =>
    resolveParameter(document, parameter, operationId),
  );
  const pathParams: Parameter[] = [];
  const queryParams: Parameter[] = [];
  for (const parameter of parameters) {
    if (parameter.in !== "path" && parameter.in !== "query") continue; // headers are the runtime's (HDR-1)
    if (parameter.name === undefined)
      throw new GeneratorError(`${operationId}: a parameter has no name.`);
    const target = parameter.in === "path" ? pathParams : queryParams;
    target.push({
      name: parameter.name,
      location: parameter.in,
      description: parameter.description ?? "",
      required: parameter.in === "path" || parameter.required === true,
      deprecated: parameter.deprecated === true,
      type: toType(document, parameter.schema ?? {}, `${operationId} parameter ${parameter.name}`),
    });
  }

  const segments = pathSegments(path, operationId, pathParams);
  const usedPathParams = pathParams.filter((parameter) =>
    segments.some((segment) => segment.kind === "param" && segment.name === parameter.name),
  );

  const body = requestBody(document, raw, operationId);
  const names = new Set<string>();
  for (const name of [...usedPathParams, ...queryParams, ...(body?.properties ?? [])].map(
    (entry) => entry.name,
  )) {
    if (names.has(name))
      throw new GeneratorError(`${operationId}: the input name ${name} is used twice.`);
    names.add(name);
  }

  const { status, type } = successResponse(document, raw, operationId);
  const paging =
    raw["x-orvano-paging"] === undefined
      ? undefined
      : pagingOf(document, operationId, raw["x-orvano-paging"].total === true, type, queryParams);
  return {
    operationId,
    service: sdk.service,
    method: sdk.method,
    httpMethod,
    path,
    segments,
    summary: raw.summary ?? "",
    description: raw.description ?? "",
    deprecated: raw.deprecated === true,
    audience: audience.toSorted(byString),
    pathParams: usedPathParams,
    queryParams,
    body,
    successStatus: status,
    response: type,
    errors: [...(raw["x-orvano-errors"] ?? [])].toSorted(byString),
    paging,
    concurrency: raw["x-orvano-concurrency"] === true,
    idempotent: raw["x-orvano-idempotent"] === true,
    metered: raw["x-orvano-metered"],
    template: raw["x-orvano-sdk-template"],
  };
}

/** PAG-1, PAG-2: a paged list takes `limit` and `cursor` and answers a `{ items, nextCursor, total }` schema. */
function pagingOf(
  document: OpenApiDocument,
  operationId: string,
  total: boolean,
  response: TypeRef | undefined,
  queryParams: Parameter[],
): Paging {
  for (const name of ["limit", "cursor"]) {
    if (!queryParams.some((parameter) => parameter.name === name && !parameter.required)) {
      throw new GeneratorError(
        `${operationId}: a paged operation takes the optional query parameter ${name} (PAG-1).`,
      );
    }
  }
  const page = response?.kind === "ref" ? response.name : undefined;
  const schema = page === undefined ? undefined : document.components?.schemas?.[page];
  const type = schema === undefined ? undefined : toType(document, schema, `Schema ${page ?? ""}`);
  const property = (name: string): Property | undefined =>
    type?.kind === "object" ? type.properties.find((entry) => entry.name === name) : undefined;
  const items = property("items")?.type;
  const nextCursor = property("nextCursor")?.type;
  if (
    page === undefined ||
    items?.kind !== "array" ||
    nextCursor?.kind !== "nullable" ||
    nextCursor.inner.kind !== "string" ||
    property("total")?.type.kind !== "nullable"
  ) {
    throw new GeneratorError(
      `${operationId}: a paged operation answers a named { items, nextCursor, total } schema (PAG-2).`,
    );
  }
  return { total, page, item: items.items };
}

/** `/v1/projects/{projectId}/...`: the project comes from the client, never from the call (PTH-5). */
function pathSegments(path: string, operationId: string, pathParams: Parameter[]): PathSegment[] {
  const parts = path.split("/").filter((part) => part.length > 0);
  return parts.map((part, index): PathSegment => {
    const param = /^\{([A-Za-z][A-Za-z0-9]*)\}$/.exec(part)?.[1];
    if (param === undefined) {
      if (part.includes("{"))
        throw new GeneratorError(
          `${operationId}: the path segment ${part} mixes text and a parameter.`,
        );
      return { kind: "literal", value: part };
    }
    if (index === 2 && parts[0] === "v1" && parts[1] === "projects" && param === "projectId")
      return { kind: "project" };
    if (!pathParams.some((parameter) => parameter.name === param)) {
      throw new GeneratorError(`${operationId}: the path parameter ${param} is not declared.`);
    }
    return { kind: "param", name: param };
  });
}

function requestBody(
  document: OpenApiDocument,
  raw: OperationObject,
  operationId: string,
): RequestBody | undefined {
  const content = raw.requestBody?.content;
  if (content === undefined) return undefined;
  const types = Object.keys(content);
  const schema = content["application/json"]?.schema;
  if (types.length !== 1 || schema === undefined) {
    throw new GeneratorError(
      `${operationId}: a request body must be application/json only, got ${types.join(", ")}.`,
    );
  }
  const where = `${operationId} request body`;
  const name = schema.$ref === undefined ? undefined : schemaRefName(document, schema.$ref, where);
  const resolved = name === undefined ? schema : (document.components?.schemas?.[name] ?? {});
  const type = toType(document, resolved, where);
  if (type.kind !== "object")
    throw new GeneratorError(`${operationId}: a request body must be a JSON object (JSW-8).`);
  return {
    schema: name,
    description: raw.requestBody?.description ?? resolved.description ?? "",
    required: raw.requestBody?.required === true,
    properties: type.properties,
  };
}

function successResponse(
  document: OpenApiDocument,
  raw: OperationObject,
  operationId: string,
): { status: number; type: TypeRef | undefined } {
  const statuses = Object.keys(raw.responses ?? {})
    .filter((key) => /^2\d\d$/.test(key))
    .toSorted(byString);
  const first = statuses[0];
  if (first === undefined)
    throw new GeneratorError(`${operationId}: the operation has no 2xx response.`);
  const status = Number(first);
  const content = raw.responses?.[first]?.content;
  if (status === 204 || content === undefined) return { status, type: undefined };
  const schema = content["application/json"]?.schema;
  if (schema === undefined) {
    throw new GeneratorError(`${operationId}: the ${first} response must be application/json.`);
  }
  return { status, type: toType(document, schema, `${operationId} response ${first}`) };
}

/** Converts a JSON Schema to a TypeRef. Anything a target could not render is a GeneratorError. */
export function toType(document: OpenApiDocument, schema: SchemaObject, where: string): TypeRef {
  if (schema.$ref !== undefined)
    return { kind: "ref", name: schemaRefName(document, schema.$ref, where) };

  const alternatives = schema.oneOf ?? schema.anyOf;
  if (alternatives !== undefined) {
    const members = alternatives.map((member) => toType(document, member, where));
    const nonNull = members.filter((member) => member.kind !== "null");
    const inner: TypeRef =
      nonNull.length === 1 && nonNull[0] !== undefined
        ? nonNull[0]
        : { kind: "union", members: nonNull };
    return nonNull.length === members.length ? inner : { kind: "nullable", inner };
  }
  if (schema.allOf !== undefined) {
    const only = schema.allOf.length === 1 ? schema.allOf[0] : undefined;
    if (only === undefined)
      throw new GeneratorError(`${where}: allOf with more than one member is not supported.`);
    return toType(document, only, where);
  }

  const types =
    schema.type === undefined ? [] : Array.isArray(schema.type) ? schema.type : [schema.type];
  const nullable = types.includes("null") || (schema.enum?.includes(null) ?? false);
  const concrete = types.filter((type) => type !== "null");
  if (concrete.length > 1)
    throw new GeneratorError(
      `${where}: a schema with types ${concrete.join(", ")} is not supported.`,
    );
  if (concrete.length === 0 && types.length > 0 && schema.enum === undefined)
    return { kind: "null" };
  const inner = concreteType(document, schema, concrete[0], where);
  return nullable ? { kind: "nullable", inner } : inner;
}

function concreteType(
  document: OpenApiDocument,
  schema: SchemaObject,
  type: string | undefined,
  where: string,
): TypeRef {
  if (schema.enum !== undefined) {
    const values = schema.enum.filter((value) => value !== null);
    if (!values.every((value): value is string => typeof value === "string")) {
      throw new GeneratorError(`${where}: enum values must be strings (JSW-2).`);
    }
    return { kind: "enum", values: [...values].toSorted(byString) };
  }
  switch (type) {
    case "string":
      return { kind: "string", format: schema.format };
    case "integer":
      return { kind: "integer", format: schema.format };
    case "number":
      return { kind: "number" };
    case "boolean":
      return { kind: "boolean" };
    case "array":
      return {
        kind: "array",
        items:
          schema.items === undefined ? { kind: "unknown" } : toType(document, schema.items, where),
      };
    case "object":
    case undefined: {
      if (schema.properties !== undefined) {
        const required = new Set(schema.required ?? []);
        return {
          kind: "object",
          properties: Object.keys(schema.properties).map((name) => {
            const property = schema.properties?.[name] ?? {};
            return {
              name,
              description: property.description ?? "",
              required: required.has(name),
              readOnly: property.readOnly === true,
              deprecated: property.deprecated === true,
              type: toType(document, property, `${where} property ${name}`),
            };
          }),
        };
      }
      const extra = schema.additionalProperties;
      if (typeof extra === "object") return { kind: "map", values: toType(document, extra, where) };
      return type === "object" ? { kind: "map", values: { kind: "unknown" } } : { kind: "unknown" };
    }
    default:
      throw new GeneratorError(`${where}: the schema type ${type} is not supported.`);
  }
}

function serviceDescription(
  document: OpenApiDocument,
  name: string,
  operations: Operation[],
): string {
  const tagNames = new Set(
    operations.flatMap(
      (operation) =>
        document.paths?.[operation.path]?.[operation.httpMethod.toLowerCase() as "get"]?.tags ?? [],
    ),
  );
  const tag =
    document.tags?.find((entry) => entry.name === name) ??
    document.tags?.find((entry) => tagNames.has(entry.name));
  return tag?.description ?? `Calls the ${name} operations.`;
}

function headerNames(document: OpenApiDocument): HeaderName[] {
  const headers = new Map<string, string>();
  for (const parameter of Object.values(document.components?.parameters ?? {})) {
    if (parameter.in === "header" && parameter.name !== undefined)
      headers.set(parameter.name, parameter.description ?? "");
  }
  for (const [name, header] of Object.entries(document.components?.headers ?? {})) {
    if (!headers.has(name)) headers.set(name, header.description ?? "");
  }
  return [...headers.keys()]
    .toSorted(byString)
    .map((name) => ({ name, description: headers.get(name) ?? "" }));
}

/** What one target sees: the operations for its audience and the schemas they reach (GNR-4). */
export interface TargetModel extends Model {
  audience: Audience;
  /** Reached schemas, each with whether it is sent (input) and read (output). */
  schemaUse: Map<string, { input: boolean; output: boolean }>;
}

export function forAudience(model: Model, audience: Audience): TargetModel {
  const services = model.services
    .map((service) => ({
      ...service,
      operations: service.operations.filter((operation) =>
        audience === "server" ? true : operation.audience.includes("client"),
      ),
    }))
    .filter((service) => service.operations.length > 0);

  const byName = new Map(model.schemas.map((schema) => [schema.name, schema]));
  const schemaUse = new Map<string, { input: boolean; output: boolean }>();
  const visit = (type: TypeRef, direction: "input" | "output"): void => {
    switch (type.kind) {
      case "ref": {
        const use = schemaUse.get(type.name) ?? { input: false, output: false };
        if (use[direction]) return;
        use[direction] = true;
        schemaUse.set(type.name, use);
        const schema = byName.get(type.name);
        if (schema !== undefined) visit(schema.type, direction);
        return;
      }
      case "array": {
        visit(type.items, direction);
        return;
      }
      case "map": {
        visit(type.values, direction);
        return;
      }
      case "nullable": {
        visit(type.inner, direction);
        return;
      }
      case "union": {
        type.members.forEach((member) => {
          visit(member, direction);
        });
        return;
      }
      case "object": {
        type.properties.forEach((property) => {
          visit(property.type, direction);
        });
        return;
      }
      default:
        return;
    }
  };
  for (const operation of services.flatMap((service) => service.operations)) {
    for (const parameter of [...operation.pathParams, ...operation.queryParams])
      visit(parameter.type, "input");
    // A body's fields are spread into the method's params type, so its own schema is not emitted (spec 0005 SHP-3).
    operation.body?.properties.forEach((property) => {
      visit(property.type, "input");
    });
    // A page is rendered as the target's own page type (PAG-6), so only its item type is reached.
    if (operation.paging !== undefined) visit(operation.paging.item, "output");
    else if (operation.response !== undefined) visit(operation.response, "output");
  }

  return {
    ...model,
    audience,
    services,
    schemas: model.schemas.filter((schema) => schemaUse.has(schema.name)),
    schemaUse,
  };
}
