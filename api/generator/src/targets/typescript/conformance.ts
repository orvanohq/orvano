// Spec 0014 CNF-3, CNF-4, STB-4: the TypeScript conformance files. The dispatcher maps each scenario call to the
// real SDK method and normalizes errors; the matcher and substitution are generated here too, so both TypeScript
// repos match the same way. Fixture services render into this file alone, never into src/.
import { GeneratorError } from "../../contract.ts";
import type { Scenario } from "../../conformance.ts";
import type { Operation, Service, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import { headerLine, type Provenance } from "../../output.ts";
import {
  clientHelpers,
  hasInputs,
  modelBlocks,
  paramsTypeName,
  serviceBody,
  serviceClassName,
} from "./files.ts";
import { fileHeader } from "./render.ts";

export function scenariosFile(scenarios: readonly Scenario[], source: Provenance): string {
  return `${JSON.stringify({ generated: headerLine(source), scenarios }, null, 2)}\n`;
}

export function dispatchFile(model: TargetModel, fixture: TargetModel, source: Provenance): string {
  const imported = new Set([
    "OrvanoError",
    "OrvanoClient",
    "RequestOptions",
    ...model.services.map(serviceClassName),
    ...model.services.flatMap((service) =>
      service.operations.filter(hasInputs).map(paramsTypeName),
    ),
  ]);
  const declared = [
    ...fixture.schemas.map((schema) => schema.name),
    ...fixture.services.map(serviceClassName),
    ...fixture.services.flatMap((service) =>
      service.operations.filter(hasInputs).map(paramsTypeName),
    ),
  ];
  for (const name of declared) {
    if (imported.has(name))
      throw new GeneratorError(`The fixture's TypeScript name ${name} is also a contract name.`);
  }
  const entries = [
    ...model.services.flatMap((service) =>
      service.operations.map((operation) => ({ service, operation })),
    ),
    ...fixture.services.flatMap((service) =>
      service.operations.map((operation) => ({ service, operation })),
    ),
  ].toSorted((a, b) => byString(callName(a.operation), callName(b.operation)));

  const publicImports = [
    "OrvanoError",
    ...model.services.map(serviceClassName),
    ...model.services.flatMap((service) =>
      service.operations.filter(hasInputs).map((operation) => `type ${paramsTypeName(operation)}`),
    ),
    "type OrvanoClient",
    "type RequestOptions",
  ].toSorted((a, b) => byString(a.replace(/^type /, ""), b.replace(/^type /, "")));

  let text = fileHeader(source);
  text +=
    "// The conformance dispatcher (spec 0014 CNF-4) and matcher (CNF-3). The fixture services below exist only in\n";
  text += "// the stub server (STB-4) and never ship in the SDK.\n";
  text += `import { ${publicImports.join(", ")} } from "../src/generated/index.js";\n`;
  if (fixture.services.length > 0) {
    text += `import { ${clientHelpers(fixture.services).join(", ")} } from "../src/runtime/client.js";\n`;
    text += 'import { request } from "../src/runtime/transport.js";\n';
    const models = modelBlocks(fixture);
    if (models !== "") text += `\n${models}`;
    for (const service of fixture.services) text += serviceBody(service, fixture);
  }
  text += staticPart;
  text += `\n/** Every call this SDK's scenarios may make, as \`<service>.<method>\`. */\n`;
  text += `export const calls: readonly string[] = [\n${entries.map(({ operation }) => `  ${JSON.stringify(callName(operation))},\n`).join("")}];\n`;
  text +=
    "\n/** Runs one scenario step's call and returns its result as JSON, or the normalized error (CNF-4). */\n";
  text +=
    "export async function dispatch(\n  client: OrvanoClient,\n  call: string,\n  params: Readonly<Record<string, unknown>>,\n  options: ConformanceOptions = {},\n): Promise<ConformanceOutcome> {\n";
  text += "  const requestOptions = toRequestOptions(options);\n  try {\n    switch (call) {\n";
  for (const { service, operation } of entries) text += dispatchCase(service, operation);
  text += "      default:\n        throw new Error(`This SDK has no call ${call}.`);\n    }\n";
  text +=
    "  } catch (error) {\n    if (OrvanoError.is(error)) return { ok: false, error: normalize(error) };\n    throw error;\n  }\n}\n";
  return text;
}

function callName(operation: Operation): string {
  return `${operation.service}.${operation.method}`;
}

function dispatchCase(service: Service, operation: Operation): string {
  const args = hasInputs(operation)
    ? `params as unknown as ${paramsTypeName(operation)}, requestOptions`
    : "requestOptions";
  const target = `new ${serviceClassName(service)}(client).${operation.method}(${args})`;
  const name = JSON.stringify(callName(operation));
  if (operation.response === undefined) {
    return `      case ${name}:\n        await ${target};\n        return { ok: true, result: null };\n`;
  }
  return `      case ${name}:\n        return { ok: true, result: toJson(await ${target}) };\n`;
}

// Written once here and copied as is: the options, error, and matching rules every TypeScript runner shares.
const staticPart = `
/** A scenario step's options (CNF-2). */
export interface ConformanceOptions {
  readonly timeout?: number | undefined;
  readonly retry?: number | undefined;
  readonly headers?: Readonly<Record<string, string>> | undefined;
  readonly idempotencyKey?: string | undefined;
  readonly ifMatch?: string | undefined;
}

/** An SDK error in the shape every SDK's dispatcher reports (CNF-4). */
export interface ConformanceError {
  readonly kind: string;
  readonly status: number | null;
  readonly code: string;
  readonly fieldErrors: readonly { field: string; location: string; code: string; message: string }[];
  readonly retryAfter: number | null;
  readonly requestId: string | null;
}

/** What one call gave: its JSON result, or its normalized error. */
export type ConformanceOutcome =
  | { readonly ok: true; readonly result: unknown }
  | { readonly ok: false; readonly error: ConformanceError };

function toRequestOptions(options: ConformanceOptions): RequestOptions {
  if (options.idempotencyKey !== undefined || options.ifMatch !== undefined) {
    throw new Error("This SDK does not offer idempotencyKey or ifMatch yet (spec 0014 build plan steps 12 and 13).");
  }
  return {
    timeout: options.timeout,
    retry: options.retry === undefined ? undefined : { maxRetries: options.retry },
    headers: options.headers,
  };
}

function normalize(error: OrvanoError): ConformanceError {
  return {
    kind: error.kind,
    status: error.status ?? null,
    code: error.code,
    fieldErrors: error.fieldErrors.map((field) => ({
      field: field.field,
      location: field.location,
      code: field.code,
      message: field.message,
    })),
    retryAfter: error.retryAfter ?? null,
    requestId: error.requestId ?? null,
  };
}

function toJson(value: unknown): unknown {
  return value === undefined ? null : (JSON.parse(JSON.stringify(value)) as unknown);
}

/** Replaces \`\${name}\` in every string with its value; an unknown name throws (CNF-3). */
export function substitute(value: unknown, variables: Readonly<Record<string, string>>): unknown {
  if (typeof value === "string") {
    return value.replace(/\\$\\{([A-Za-z][A-Za-z0-9]*)\\}/g, (_, name: string) => {
      const replacement = variables[name];
      if (replacement === undefined) throw new Error(\`The scenario names \\\${\${name}}, which is not set.\`);
      return replacement;
    });
  }
  if (Array.isArray(value)) return value.map((item) => substitute(item, variables));
  if (typeof value === "object" && value !== null) {
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, substitute(item, variables)]));
  }
  return value;
}

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const timestampPattern = /^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d+)?(Z|[+-]\\d{2}:\\d{2})$/;

/**
 * Matches a value against an expectation (CNF-3): objects by subset, arrays exactly unless \`$contains\`, and the
 * matchers \`$any\`, \`$uuid\`, \`$timestamp\`, and \`$regex\`. Returns one message per mismatch; empty means a match.
 */
export function match(expected: unknown, actual: unknown, path = "$"): string[] {
  if (isRecord(expected)) {
    const keys = Object.keys(expected);
    const matcher = keys.length === 1 && keys[0]?.startsWith("$") === true ? keys[0] : undefined;
    if (matcher !== undefined) return matchWith(matcher, expected[matcher], actual, path);
    if (!isRecord(actual)) return [\`\${path}: expected an object, got \${describe(actual)}\`];
    return keys.flatMap((key) =>
      key in actual ? match(expected[key], actual[key], \`\${path}.\${key}\`) : [\`\${path}.\${key}: missing\`],
    );
  }
  if (Array.isArray(expected)) {
    if (!Array.isArray(actual)) return [\`\${path}: expected an array, got \${describe(actual)}\`];
    if (actual.length !== expected.length) {
      return [\`\${path}: expected \${String(expected.length)} items, got \${String(actual.length)}\`];
    }
    return expected.flatMap((item, index) => match(item, actual[index], \`\${path}[\${String(index)}]\`));
  }
  return Object.is(expected, actual) ? [] : [\`\${path}: expected \${describe(expected)}, got \${describe(actual)}\`];
}

function matchWith(matcher: string, argument: unknown, actual: unknown, path: string): string[] {
  switch (matcher) {
    case "$any":
      return actual === undefined ? [\`\${path}: missing\`] : [];
    case "$uuid":
      return typeof actual === "string" && uuidPattern.test(actual) ? [] : [\`\${path}: expected a uuid, got \${describe(actual)}\`];
    case "$timestamp":
      return typeof actual === "string" && timestampPattern.test(actual) && !Number.isNaN(Date.parse(actual))
        ? []
        : [\`\${path}: expected an RFC 3339 timestamp, got \${describe(actual)}\`];
    case "$regex":
      return typeof actual === "string" && new RegExp(String(argument)).test(actual)
        ? []
        : [\`\${path}: expected a match for /\${String(argument)}/, got \${describe(actual)}\`];
    case "$contains": {
      if (!Array.isArray(actual)) return [\`\${path}: expected an array, got \${describe(actual)}\`];
      const wanted = Array.isArray(argument) ? argument : [argument];
      return wanted.flatMap((item, index) =>
        actual.some((candidate) => match(item, candidate).length === 0)
          ? []
          : [\`\${path}: no item matches $contains[\${String(index)}] \${describe(item)}\`],
      );
    }
    default:
      return [\`\${path}: unknown matcher \${matcher}\`];
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function describe(value: unknown): string {
  return value === undefined ? "nothing" : JSON.stringify(value);
}
`;
