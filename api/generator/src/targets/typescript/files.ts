// The files the TypeScript targets write under src/generated/ (spec 0014 GNR-6, spec 0005 CGN-1).
import { GeneratorError } from "../../contract.ts";
import type {
  NamedSchema,
  Operation,
  Parameter,
  Property,
  Service,
  TargetModel,
  TypeRef,
} from "../../model.ts";
import { byString, camelCase, kebabCase, pascalCase } from "../../naming.ts";
import type { Provenance } from "../../output.ts";
import { fileHeader, propertyKey, tsDoc, tsType } from "./render.ts";

/** Names the runtime exports through the entry point; generated names must not take them. */
export const runtimePublicNames: readonly string[] = [
  "CommonClientOptions",
  "ErrorKind",
  "FieldError",
  "OrvanoClient",
  "OrvanoError",
  "OrvanoErrorJson",
  "RequestOptions",
];

/** Header constants the runtime reads, so the contract must declare them (HDR-4). */
const runtimeHeaders: readonly string[] = [
  "Authorization",
  "Idempotency-Key",
  "Orvano-Api-Version",
  "Orvano-Client",
  "Orvano-Key",
  "Orvano-Request-Id",
  "Retry-After",
];

export function serviceClassName(service: Service): string {
  return pascalCase(service.name);
}

export function serviceFileName(service: Service): string {
  return `${kebabCase(service.name)}-service.ts`;
}

export function paramsTypeName(operation: Operation): string {
  return `${pascalCase(operation.method)}${pascalCase(operation.service)}Params`;
}

export function hasInputs(operation: Operation): boolean {
  return (
    operation.pathParams.length +
      operation.queryParams.length +
      (operation.body?.properties.length ?? 0) >
    0
  );
}

/** Fails when two generated names, or a generated and a runtime name, would be the same export. */
export function checkNames(model: TargetModel): void {
  const owners = new Map<string, string>();
  const claim = (name: string, owner: string): void => {
    const other = owners.get(name);
    if (other !== undefined)
      throw new GeneratorError(`${owner}: the TypeScript name ${name} is also used by ${other}.`);
    owners.set(name, owner);
  };
  for (const name of [
    ...runtimePublicNames,
    "OrvanoErrorCode",
    "OrvanoErrorCodes",
    "OrvanoHeaders",
    "apiVersion",
    "sdkPackage",
  ]) {
    claim(name, "the runtime");
  }
  for (const schema of model.schemas) claim(schema.name, `schema ${schema.name}`);
  for (const service of model.services) {
    claim(serviceClassName(service), `service ${service.name}`);
    for (const operation of service.operations) {
      if (["client", "constructor"].includes(operation.method)) {
        throw new GeneratorError(
          `${operation.operationId}: ${operation.method} can't be a TypeScript method name.`,
        );
      }
      if (hasInputs(operation)) claim(paramsTypeName(operation), operation.operationId);
    }
  }
  const declared = new Set(model.headers.map((header) => header.name));
  for (const header of runtimeHeaders) {
    if (!declared.has(header))
      throw new GeneratorError(`The contract does not declare the header ${header} (HDR-4).`);
  }
}

export function modelsFile(model: TargetModel, source: Provenance): string {
  const blocks = model.schemas.map((schema) =>
    modelBlock(schema, model.schemaUse.get(schema.name)?.input === true),
  );
  return `${fileHeader(source)}\n${blocks.join("\n")}`;
}

function modelBlock(schema: NamedSchema, input: boolean): string {
  const where = `Schema ${schema.name}`;
  const doc = tsDoc([schema.description], ["@public"]);
  if (schema.type.kind === "object") {
    const members = schema.type.properties
      .map((property) => propertyMember(property, input, where))
      .join("");
    return `${doc}export interface ${schema.name} {\n${members}}\n`;
  }
  return `${doc}export type ${schema.name} = ${tsType(schema.type, where)};\n`;
}

function propertyMember(property: Property, input: boolean, where: string): string {
  const tags = property.deprecated ? ["@deprecated"] : [];
  const optional = property.required ? "" : "?";
  // exactOptionalPropertyTypes: an input field may also be passed as undefined.
  const undefinedType = !property.required && input ? " | undefined" : "";
  const type = tsType(property.type, `${where} property ${property.name}`);
  return `${tsDoc([property.description], tags, "  ")}  ${propertyKey(property.name)}${optional}: ${type}${undefinedType};\n`;
}

export function serviceFile(service: Service, source: Provenance, model: TargetModel): string {
  const className = serviceClassName(service);
  const operations = service.operations;
  const uses = (kind: "project" | "param"): boolean =>
    operations.some((operation) => operation.segments.some((segment) => segment.kind === kind));

  const clientImports = [
    "assertClient",
    ...(uses("param") ? ["pathParam"] : []),
    ...(uses("project") ? ["projectId"] : []),
  ];
  const modelImports = new Set<string>();
  for (const operation of operations) {
    collectRefs(operation, modelImports);
  }

  let text = fileHeader(source);
  text += `import { ${clientImports.join(", ")}, type OrvanoClient, type RequestOptions } from "../runtime/client.js";\n`;
  text += `import { request } from "../runtime/transport.js";\n`;
  if (modelImports.size > 0) {
    text += `import type { ${[...modelImports].toSorted(byString).join(", ")} } from "./models.js";\n`;
  }
  for (const operation of operations) {
    if (hasInputs(operation)) text += `\n${paramsInterface(operation, model)}`;
  }
  text += `\n${tsDoc([service.description], ["@public"])}`;
  text += `export class ${className} {\n`;
  text += `  private readonly client: OrvanoClient;\n\n`;
  text += tsDoc(
    [
      `Creates the service over a client from \`createClient\`. Throws \`TypeError\` for a client from another Orvano package.`,
    ],
    [],
    "  ",
  );
  text += `  constructor(client: OrvanoClient) {\n    assertClient(client, ${JSON.stringify(className)});\n    this.client = client;\n  }\n`;
  for (const operation of operations) text += `\n${method(operation)}`;
  text += "}\n";
  return text;
}

function collectRefs(operation: Operation, names: Set<string>): void {
  const visit = (type: TypeRef): void => {
    switch (type.kind) {
      case "ref":
        names.add(type.name);
        return;
      case "array": {
        visit(type.items);
        return;
      }
      case "map": {
        visit(type.values);
        return;
      }
      case "nullable": {
        visit(type.inner);
        return;
      }
      case "union": {
        type.members.forEach(visit);
        return;
      }
      case "object": {
        type.properties.forEach((property) => {
          visit(property.type);
        });
        return;
      }
      default:
        return;
    }
  };
  for (const parameter of [...operation.pathParams, ...operation.queryParams])
    visit(parameter.type);
  for (const property of operation.body?.properties ?? []) visit(property.type);
  if (operation.response !== undefined) visit(operation.response);
}

function paramsInterface(operation: Operation, model: TargetModel): string {
  const where = operation.operationId;
  let text = tsDoc(
    [`The inputs of \`${camelCase(operation.service)}.${operation.method}\`.`],
    ["@public"],
  );
  text += `export interface ${paramsTypeName(operation)} {\n`;
  for (const parameter of [...operation.pathParams, ...operation.queryParams])
    text += parameterMember(parameter, where, model);
  for (const property of operation.body?.properties ?? []) {
    if (property.readOnly) continue;
    text += propertyMember(property, true, `${where} body`);
  }
  return `${text}}\n`;
}

function parameterMember(parameter: Parameter, where: string, model: TargetModel): string {
  const type = tsType(parameter.type, `${where} parameter ${parameter.name}`);
  if (parameter.location === "query" && !queryable(parameter.type, model)) {
    throw new GeneratorError(
      `${where}: the query parameter ${parameter.name} must be a string, number, boolean, or an array of them.`,
    );
  }
  const tags = parameter.deprecated ? ["@deprecated"] : [];
  const optional = parameter.required ? "" : "?";
  const undefinedType = parameter.required ? "" : " | undefined";
  return `${tsDoc([parameter.description], tags, "  ")}  ${propertyKey(parameter.name)}${optional}: ${type}${undefinedType};\n`;
}

function queryable(type: TypeRef, model: TargetModel): boolean {
  const resolved = (entry: TypeRef): TypeRef =>
    entry.kind === "ref"
      ? (model.schemas.find((schema) => schema.name === entry.name)?.type ?? entry)
      : entry;
  const scalar = (entry: TypeRef): boolean =>
    ["string", "integer", "number", "boolean", "enum"].includes(resolved(entry).kind);
  const outer = resolved(type);
  return scalar(outer) || (outer.kind === "array" && scalar(outer.items));
}

function method(operation: Operation): string {
  const responseType =
    operation.response === undefined ? "void" : tsType(operation.response, operation.operationId);
  const inputs = hasInputs(operation);
  const allOptional =
    inputs &&
    [
      ...operation.pathParams,
      ...operation.queryParams,
      ...(operation.body?.properties ?? []),
    ].every((entry) => !entry.required);
  const tags = [
    ...(inputs ? ["@param params - The call's inputs."] : []),
    "@param options - Per call options: signal, timeout, headers, and retry.",
    `@throws {@link OrvanoError} when the call fails.`,
    ...(operation.deprecated ? ["@deprecated"] : []),
  ];
  const signature = inputs
    ? `params: ${paramsTypeName(operation)}${allOptional ? " = {}" : ""}, options?: RequestOptions`
    : "options?: RequestOptions";

  const path = operation.segments.map((segment) => {
    switch (segment.kind) {
      case "literal":
        return JSON.stringify(segment.value);
      case "project":
        return "projectId(this.client)";
      case "param":
        return `pathParam(${JSON.stringify(segment.name)}, params.${segment.name})`;
    }
  });
  const fields = [
    `operationId: ${JSON.stringify(operation.operationId)}`,
    `method: ${JSON.stringify(operation.httpMethod)}`,
    `path: [${path.join(", ")}]`,
  ];
  if (operation.queryParams.length > 0) {
    fields.push(
      `query: { ${operation.queryParams.map((parameter) => `${propertyKey(parameter.name)}: params.${accessor(parameter.name)}`).join(", ")} }`,
    );
  }
  if (operation.body !== undefined) {
    const members = operation.body.properties
      .filter((property) => !property.readOnly)
      .map((property) => `${propertyKey(property.name)}: params.${accessor(property.name)}`);
    fields.push(`body: { ${members.join(", ")} }`);
  }
  fields.push("options");

  let text = tsDoc([operation.summary, operation.description], tags, "  ");
  text += `  async ${operation.method}(${signature}): Promise<${responseType}> {\n`;
  text += `    return request<${responseType}>(this.client, {\n`;
  text += fields.map((field) => `      ${field},\n`).join("");
  text += "    });\n  }\n";
  return text;
}

function accessor(name: string): string {
  return /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) ? name : `[${JSON.stringify(name)}]`;
}

export function errorCodesFile(model: TargetModel, source: Provenance): string {
  const codes = [
    ...model.errorCodes,
    {
      code: "unknown",
      status: 0,
      description: "SDK only: the error body was not a problem, or the call never got an answer.",
      members: [],
    },
  ].toSorted((a, b) => byString(a.code, b.code));
  let text = fileHeader(source);
  text += `\n${tsDoc(["Every error code the API sends, plus `unknown`. Open, so a newer server's code is still a string."], ["@public"])}`;
  text += `export type OrvanoErrorCode =\n${codes.map((entry) => `  | ${JSON.stringify(entry.code)}\n`).join("")}  | (string & {});\n`;
  text += `\n${tsDoc(["The error codes as constants, to compare with `OrvanoError.code`."], ["@public"])}`;
  text += "export const OrvanoErrorCodes = {\n";
  for (const entry of codes) {
    const status = entry.status === 0 ? "" : `${String(entry.status)}: `;
    text += `${tsDoc([`${status}${entry.description}`], [], "  ")}  ${camelCase(entry.code)}: ${JSON.stringify(entry.code)},\n`;
  }
  return `${text}} as const;\n`;
}

export function contractFile(model: TargetModel, source: Provenance, sdkPackage: string): string {
  let text = fileHeader(source);
  text += `\n${tsDoc(["The API version this SDK was generated for, sent as `Orvano-Api-Version` on every request."], ["@public"])}`;
  text += `export const apiVersion = ${JSON.stringify(model.version)};\n`;
  text += `\n${tsDoc(["The npm package this code was generated into."], ["@public"])}`;
  text += `export const sdkPackage = ${JSON.stringify(sdkPackage)};\n`;
  text += `\n${tsDoc(["Orvano's HTTP header names (spec 0014 HDR-1)."], ["@public"])}`;
  text += "export const OrvanoHeaders = {\n";
  for (const header of model.headers) {
    text += `${tsDoc([header.description], [], "  ")}  ${camelCase(header.name)}: ${JSON.stringify(header.name)},\n`;
  }
  return `${text}} as const;\n`;
}

export function indexFile(model: TargetModel, source: Provenance): string {
  let text = fileHeader(source);
  text +=
    "// Deviation SDK-5: the one generated barrel, so a new service reaches the entry point without a hand edit.\n";
  text += 'export { apiVersion, OrvanoHeaders, sdkPackage } from "./contract.js";\n';
  text += 'export { OrvanoErrorCodes, type OrvanoErrorCode } from "./error-codes.js";\n';
  if (model.schemas.length > 0) {
    text += `export type { ${model.schemas.map((schema) => schema.name).join(", ")} } from "./models.js";\n`;
  }
  for (const service of model.services) {
    const params = service.operations
      .filter(hasInputs)
      .map((operation) => `type ${paramsTypeName(operation)}`);
    const names = [serviceClassName(service), ...params.toSorted(byString)].join(", ");
    text += `export { ${names} } from "./${serviceFileName(service).replace(/\.ts$/, ".js")}";\n`;
  }
  text += 'export type { OrvanoClient, RequestOptions } from "../runtime/client.js";\n';
  text +=
    'export { OrvanoError, type ErrorKind, type FieldError, type OrvanoErrorJson } from "../runtime/errors.js";\n';
  text += 'export type { CommonClientOptions } from "../runtime/options.js";\n';
  return text;
}
