// The files the Dart targets write under lib/src/generated/ (spec 0014 GNR-6, spec 0004 MDL-1 to MDL-3, CLN-2).
import { GeneratorError } from "../../contract.ts";
import type { Operation, Parameter, Property, Service, TargetModel } from "../../model.ts";
import { byString, camelCase, pascalCase, snakeCase, words } from "../../naming.ts";
import type { Provenance } from "../../output.ts";
import {
  checkMemberName,
  dartDoc,
  dartString,
  dartTypeName,
  fileHeader,
  isCollection,
  nonNull,
  readExpression,
  readerFunction,
  writeExpression,
  writeNullable,
} from "./render.ts";
import { holdsEnum, type DartDeclaration, type DartType, type DartTypes } from "./types.ts";

/** Public names of the shared runtime (lib/src/core/), exported by the generated barrel. */
export const corePublicNames: Readonly<Record<string, readonly string[]>> = {
  "cancel_token.dart": ["OrvanoCancelReason", "OrvanoCancelToken"],
  "codec.dart": ["OrvanoCodec"],
  "exceptions.dart": [
    "OrvanoApiException",
    "OrvanoCancelledException",
    "OrvanoErrorKind",
    "OrvanoException",
    "OrvanoFieldError",
    "OrvanoNetworkException",
    "OrvanoProtocolException",
    "OrvanoRateLimitedException",
    "OrvanoTimeoutException",
  ],
  "optional.dart": ["Optional"],
  "options.dart": ["OrvanoClientOptions", "OrvanoRequestOptions", "RetryOptions"],
  "paging.dart": ["ResultPage"],
};

/** Names the runtime and the hand written layers already use, so no schema or service may take them. */
const takenNames: readonly string[] = [
  ...Object.values(corePublicNames).flat(),
  "AuthHeaderProvider",
  "KeyAuth",
  "OrvanoErrorCodes",
  "OrvanoHeaders",
  "OrvanoServices",
  "OrvanoTransport",
  // the hand written clients and layers (spec 0004 CLN-1, CLN-4, MDL-5, SES-1)
  "Orvano",
  "OrvanoFlutter",
  "OrvanoServer",
  "OrvanoRow",
  "TableHandle",
  "SessionStore",
  // Flutter widgets an app imports beside the SDK (spec 0004 CLN-4)
  "Page",
  "Row",
];

/** Header constants the runtime reads, so the contract must declare them (HDR-4). */
const runtimeHeaders: readonly string[] = [
  "Authorization",
  "Idempotency-Key",
  "If-Match",
  "Orvano-Api-Version",
  "Orvano-Client",
  "Orvano-Key",
  "Orvano-Request-Id",
  "Retry-After",
];

/** Error codes the runtime compares with, so the catalogue must have them (spec 0004 NET-8). */
const runtimeErrorCodes: readonly string[] = ["session_expired"];

export function serviceClassName(service: Service): string {
  return `${pascalCase(service.name)}Service`;
}

export function serviceFileName(service: Service): string {
  return `${snakeCase(service.name)}_service.dart`;
}

/** Fails when two generated names, or a generated and a runtime name, would be the same Dart name. */
export function checkNames(model: TargetModel, types: DartTypes): void {
  const owners = new Map<string, string>();
  const claim = (name: string, owner: string): void => {
    const other = owners.get(name);
    if (other !== undefined)
      throw new GeneratorError(`${owner}: the Dart name ${name} is also used by ${other}.`);
    owners.set(name, owner);
  };
  for (const name of takenNames) claim(name, "the runtime");
  for (const declaration of types.declarations())
    claim(declaration.name, `schema ${declaration.name}`);
  for (const service of model.services) {
    claim(serviceClassName(service), `service ${service.name}`);
    checkMemberName(service.name, `service ${service.name}`);
    if (["transport", "close", "auth"].includes(service.name)) {
      throw new GeneratorError(
        `service ${service.name}: ${service.name} is a client member, not a service name.`,
      );
    }
  }
  const declared = new Set(model.headers.map((header) => header.name));
  for (const header of runtimeHeaders) {
    if (!declared.has(header))
      throw new GeneratorError(`The contract does not declare the header ${header} (HDR-4).`);
  }
  const codes = new Set(model.errorCodes.map((entry) => entry.code));
  for (const code of runtimeErrorCodes) {
    if (!codes.has(code))
      throw new GeneratorError(`The contract's x-orvano-error-codes has no ${code}.`);
  }
}

// ---------------------------------------------------------------------------------------------------------------
// Models

/** Every enum and class the types reach, in one file's body (without imports). */
export function declarationBlocks(types: DartTypes): string {
  return types
    .declarations()
    .map((declaration) =>
      declaration.kind === "enum" ? enumBlock(declaration) : classBlock(declaration, types),
    )
    .join("\n");
}

function enumMemberName(value: string, where: string): string {
  const name = camelCase(value);
  if (name === "" || !/^[a-z]/.test(name)) {
    throw new GeneratorError(`${where}: the enum value ${value} can't be a Dart name.`);
  }
  if (name === "unknown" || name === "values" || name === "index") {
    throw new GeneratorError(`${where}: the enum value ${value} takes a name Dart enums reserve.`);
  }
  return checkMemberName(name, where);
}

function enumBlock(declaration: Extract<DartDeclaration, { kind: "enum" }>): string {
  const { name } = declaration;
  const where = `Enum ${name}`;
  const seen = new Set<string>();
  let text = dartDoc([
    declaration.description,
    "A value a newer server sends that this SDK does not know reads as [unknown] (spec 0004 MDL-3).",
  ]);
  text += `enum ${name} {\n`;
  for (const value of declaration.values) {
    const member = enumMemberName(value, where);
    if (seen.has(member))
      throw new GeneratorError(`${where}: two values give the Dart name ${member}.`);
    seen.add(member);
    text += `${dartDoc([`The \`${value}\` value.`], "  ")}  ${member}(${dartString(value)}),\n\n`;
  }
  text += `${dartDoc(["A value this SDK does not know, from a newer server. It can't be sent."], "  ")}  unknown(null);\n\n`;
  text += `  const ${name}(this.wireName);\n\n`;
  text += dartDoc(["The value on the wire, or null for [unknown]."], "  ");
  text += "  final String? wireName;\n\n";
  text += dartDoc(["Reads a wire value; one this SDK does not know gives [unknown]."], "  ");
  text += `  static ${name} fromJson(String value) {\n`;
  text += `    for (final candidate in values) {\n      if (candidate.wireName == value) return candidate;\n    }\n    return unknown;\n  }\n\n`;
  text += dartDoc(["The value on the wire. Throws `ArgumentError` for [unknown]."], "  ");
  text += `  String toJson() =>\n      wireName ??\n      (throw ArgumentError.value(this, 'value', 'is unknown and can not be sent'));\n}\n`;
  return text;
}

interface Field {
  name: string;
  property: Property;
  type: DartType;
  /** Keeps the raw JSON, so an unknown enum value survives toJson. */
  raw: boolean;
}

function fieldsOf(
  declaration: Extract<DartDeclaration, { kind: "class" }>,
  types: DartTypes,
): Field[] {
  const where = `Schema ${declaration.name}`;
  return declaration.properties.map((property) => {
    const name = checkMemberName(property.name, `${where} property ${property.name}`);
    const type = types.property(
      property,
      declaration.name,
      declaration.output,
      `${where} property ${property.name}`,
    );
    return { name, property, type, raw: declaration.output && holdsEnum(type) };
  });
}

/** A field the constructor requires: one that can't be null. */
function isRequiredField(field: Field): boolean {
  return field.type.kind !== "nullable" && field.type.kind !== "any";
}

/** Field names whose values `toString` never shows (spec 0004 DSC-2, MDL-1). */
const secretName = /token|secret|password|^key$|apiKey/i;

function classBlock(
  declaration: Extract<DartDeclaration, { kind: "class" }>,
  types: DartTypes,
): string {
  const { name } = declaration;
  const fields = fieldsOf(declaration, types);
  const raws = fields.filter((field) => field.raw);
  const params =
    fields.length === 0
      ? ""
      : `{\n${fields.map((field) => `    ${isRequiredField(field) ? "required " : ""}this.${field.name},\n`).join("")}  }`;

  let text = dartDoc([declaration.description]);
  text += `final class ${name} {\n`;
  text += dartDoc([`Creates a [${name}].`], "  ");
  text += `  const ${name}(${params})${raws.length > 0 ? `\n    : ${raws.map((field) => `_${field.name}Json = null`).join(",\n      ")}` : ""};\n\n`;
  if (raws.length > 0) {
    text += `  const ${name}._(${fields.length === 0 ? "" : `{\n${fields.map((field) => `    required this.${field.name},\n`).join("")}${raws.map((field) => `    required Object? ${field.name}Json,\n`).join("")}  }`})\n    : ${raws.map((field) => `_${field.name}Json = ${field.name}Json`).join(",\n      ")};\n\n`;
  }

  text += dartDoc(
    [
      `Reads a [${name}] from JSON. A missing or mistyped field throws [OrvanoProtocolException] naming its path; unknown fields are ignored.`,
    ],
    "  ",
  );
  const constructor = raws.length > 0 ? `${name}._` : name;
  text += `  factory ${name}.fromJson(Map<String, Object?> json, [String path = r'$']) => ${constructor}(\n`;
  for (const field of fields) {
    text += `    ${field.name}: ${readExpression(field.type, `json[${dartString(field.property.name)}]`, `'$path.${field.property.name}'`)},\n`;
  }
  for (const field of raws)
    text += `    ${field.name}Json: json[${dartString(field.property.name)}],\n`;
  text += "  );\n";

  for (const field of fields) {
    const doc =
      field.property.description === ""
        ? `The \`${field.property.name}\` field.`
        : field.property.description;
    text += `\n${dartDoc([doc], "  ")}`;
    if (field.property.deprecated)
      text += "  @Deprecated('The contract marks this field deprecated.')\n";
    text += `  final ${dartTypeName(field.type)} ${field.name};\n`;
  }
  for (const field of raws) text += `\n  final Object? _${field.name}Json;\n`;

  text += `\n${dartDoc(["This value as JSON, the shape the server sends."], "  ")}`;
  text += `  Map<String, Object?> toJson() => {\n`;
  for (const field of fields) {
    const written = writeExpression(field.type, field.name);
    text += `    ${dartString(field.property.name)}: ${field.raw ? `_${field.name}Json ?? ${written}` : written},\n`;
  }
  text += "  };\n";

  text += `\n${dartDoc(["A copy with the given fields replaced. It can't set a field to null; use the constructor for that."], "  ")}`;
  if (fields.length === 0) {
    text += `  ${name} copyWith() => const ${name}();\n`;
  } else {
    text += `  ${name} copyWith({\n${fields.map((field) => `    ${dartTypeName(nonNull(field.type))}${field.type.kind === "any" ? "" : "?"} ${field.name},\n`).join("")}  }) => ${constructor}(\n`;
    for (const field of fields) text += `    ${field.name}: ${field.name} ?? this.${field.name},\n`;
    for (const field of raws)
      text += `    ${field.name}Json: ${field.name} == null ? _${field.name}Json : null,\n`;
    text += "  );\n";
  }

  text += "\n  @override\n  bool operator ==(Object other) =>\n";
  text += `      identical(this, other) ||\n      other is ${name}`;
  for (const field of fields) {
    text += isCollection(field.type)
      ? ` &&\n          _equality.equals(other.${field.name}, ${field.name})`
      : ` &&\n          other.${field.name} == ${field.name}`;
  }
  text += ";\n\n  @override\n";
  text +=
    fields.length === 0
      ? `  int get hashCode => ${dartString(name)}.hashCode;\n`
      : `  int get hashCode => Object.hashAll([\n${fields.map((field) => `    ${isCollection(field.type) ? `_equality.hash(${field.name})` : field.name},\n`).join("")}  ]);\n`;

  text += "\n  @override\n";
  const shown = fields.map((field) =>
    secretName.test(field.name) ? `${field.name}: <redacted>` : `${field.name}: $${field.name}`,
  );
  text += `  String toString() => '${name}(${shown.join(", ")})';\n`;
  return `${text}}\n`;
}

export function modelsFile(types: DartTypes, source: Provenance): string {
  const blocks = declarationBlocks(types);
  let text = fileHeader(source);
  if (blocks === "") return `${text}\n// The contract reaches no models for this SDK.\n`;
  text += "\n";
  if (blocks.includes("_equality")) text += "import 'package:collection/collection.dart';\n\n";
  text += "import '../core/exceptions.dart';\nimport '../core/json.dart';\n\n";
  if (blocks.includes("_equality"))
    text += "const DeepCollectionEquality _equality = DeepCollectionEquality();\n\n";
  return text + blocks;
}

// ---------------------------------------------------------------------------------------------------------------
// Services

interface Input {
  name: string;
  wireName: string;
  description: string;
  required: boolean;
  deprecated: boolean;
  type: DartType;
  location: "path" | "query" | "body";
  /** A partial update's field (JSW-10): an `Optional<type>`, absent by default and then not sent. */
  optional: boolean;
}

/** A type a path or query parameter can hold. */
function isScalar(type: DartType): boolean {
  return ["string", "int", "double", "bool", "dateTime", "enum"].includes(type.kind);
}

/** An operation's inputs as named parameters: path, query, then body fields (spec 0004 CLN-3). */
export function inputsOf(operation: Operation, types: DartTypes): Input[] {
  const where = operation.operationId;
  const owner = `${pascalCase(operation.method)}${pascalCase(operation.service)}`;
  const parameter = (entry: Parameter): Input => {
    const resolved = types.resolve(
      entry.type,
      `${owner}${pascalCase(entry.name)}`,
      false,
      `${where} parameter ${entry.name}`,
      entry.description,
    );
    const type =
      entry.required || resolved.kind === "nullable" || resolved.kind === "any"
        ? resolved
        : { kind: "nullable" as const, inner: resolved };
    const plain = nonNull(type);
    if (
      !(
        isScalar(plain) ||
        (plain.kind === "list" && isScalar(plain.items) && entry.location === "query")
      )
    ) {
      throw new GeneratorError(
        `${where}: the ${entry.location} parameter ${entry.name} must be a string, number, boolean, or enum${entry.location === "query" ? ", or a list of them" : ""}.`,
      );
    }
    return {
      name: checkMemberName(entry.name, `${where} parameter ${entry.name}`),
      wireName: entry.name,
      description: entry.description,
      required: entry.required,
      deprecated: entry.deprecated,
      type,
      location: entry.location,
      optional: false,
    };
  };
  const bodyOwner = operation.body?.schema ?? `${owner}Request`;
  const inputs = [
    ...operation.pathParams.map(parameter),
    ...operation.queryParams.map(parameter),
    ...(operation.body?.properties ?? [])
      .filter((property) => !property.readOnly)
      .map((property): Input => {
        const at = `${where} body property ${property.name}`;
        // JSW-10: in a PATCH, an optional field is Optional<T>, nullable only when the contract's type is.
        const optional = operation.body?.partial === true && !property.required;
        return {
          name: checkMemberName(property.name, at),
          wireName: property.name,
          description: property.description,
          required: property.required,
          deprecated: property.deprecated,
          type: optional
            ? types.resolve(
                property.type,
                `${bodyOwner}${pascalCase(property.name)}`,
                false,
                at,
                property.description,
              )
            : types.property(property, bodyOwner, false, at),
          location: "body",
          optional,
        };
      }),
  ];
  if (
    inputs.some(
      (input) =>
        input.name === "options" ||
        (operation.concurrency && input.name === "ifMatch") ||
        (operation.idempotent && input.name === "idempotencyKey"),
    )
  ) {
    throw new GeneratorError(
      `${where}: an input named options, ifMatch, or idempotencyKey clashes with a request option parameter.`,
    );
  }
  return inputs;
}

/** The Dart method name of an operation. */
export function methodName(operation: Operation): string {
  return checkMemberName(operation.method, operation.operationId);
}

/** A service class, without imports. `transport` is how it reaches its transport. */
export function serviceClass(service: Service, types: DartTypes): string {
  const className = serviceClassName(service);
  let text = dartDoc([service.description]);
  text += `final class ${className} {\n`;
  text += dartDoc(
    [
      `Creates the service over a client's transport. Apps get it from the client (\`orvano.${service.name}\`).`,
    ],
    "  ",
  );
  text += `  @internal\n  ${className}(this._transport);\n\n  final OrvanoTransport _transport;\n`;
  for (const operation of service.operations) text += `\n${method(operation, types)}`;
  return `${text}}\n`;
}

/** A paged list's item type (PAG-6). */
export function pageItemOf(operation: Operation, types: DartTypes): DartType | undefined {
  return operation.paging === undefined
    ? undefined
    : types.resolve(
        operation.paging.item,
        `${pascalCase(operation.method)}${pascalCase(operation.service)}Item`,
        true,
        `${operation.operationId} item`,
      );
}

function method(operation: Operation, types: DartTypes): string {
  const where = operation.operationId;
  const inputs = inputsOf(operation, types);
  const item = pageItemOf(operation, types);
  const response =
    operation.response === undefined || item !== undefined
      ? undefined
      : types.resolve(
          operation.response,
          `${pascalCase(operation.method)}${pascalCase(operation.service)}Response`,
          true,
          `${where} response`,
        );
  const returnType =
    item !== undefined
      ? `ResultPage<${dartTypeName(item)}>`
      : response === undefined
        ? "void"
        : dartTypeName(response);
  const name = methodName(operation);

  const paragraphs = [operation.summary, operation.description];
  for (const input of inputs) {
    if (input.description !== "") paragraphs.push(`[${input.name}]: ${input.description}`);
  }
  if (operation.concurrency) {
    paragraphs.push(
      "[ifMatch]: the resource's `version` as you read it. The write fails with `version_conflict` when it changed since; leave it out to write anyway.",
    );
  }
  if (operation.idempotent) {
    paragraphs.push(
      "[idempotencyKey]: a key you choose, 1 to 255 printable ASCII characters, sent as `Idempotency-Key`. With it, a failed call is retried; reuse it only for the same request.",
    );
  }
  paragraphs.push("Throws an [OrvanoException] when the call fails.");
  let text = dartDoc(paragraphs, "  ");
  if (operation.deprecated)
    text += "  @Deprecated('The contract marks this operation deprecated.')\n";
  const params = [
    ...inputs.map((input) =>
      input.optional
        ? `Optional<${dartTypeName(input.type)}> ${input.name} = const Optional.absent()`
        : `${input.required ? "required " : ""}${dartTypeName(input.type)} ${input.name}`,
    ),
    ...(operation.concurrency ? ["String? ifMatch"] : []),
    ...(operation.idempotent ? ["String? idempotencyKey"] : []),
    "OrvanoRequestOptions? options",
  ];
  text += `  Future<${returnType}> ${name}({\n${params.map((param) => `    ${param},\n`).join("")}  }) async {\n`;

  const path = operation.segments.map((segment) => {
    switch (segment.kind) {
      case "literal":
        return dartString(segment.value);
      case "project":
        return "_transport.projectId";
      case "param": {
        const input = inputs.find(
          (entry) => entry.location === "path" && entry.wireName === segment.name,
        );
        if (input === undefined)
          throw new GeneratorError(`${where}: the path parameter ${segment.name} is not declared.`);
        return `pathSegment(${dartString(input.name)}, ${pathValue(input)})`;
      }
    }
  });
  const fields = [
    `operationId: ${dartString(operation.operationId)}`,
    `method: ${dartString(operation.httpMethod)}`,
    `path: [${path.join(", ")}]`,
  ];
  const query = inputs.filter((input) => input.location === "query");
  if (query.length > 0) {
    fields.push(
      `query: {${query.map((input) => `${dartString(input.wireName)}: ${valueExpression(input)}`).join(", ")}}`,
    );
  }
  if (operation.body !== undefined) {
    const members = inputs
      .filter((input) => input.location === "body")
      .map((input) => {
        if (input.optional) {
          const value = `${input.name}.value`;
          const written =
            input.type.kind === "nullable"
              ? writeNullable(input.type, value)
              : writeExpression(input.type, value);
          return `if (${input.name}.isPresent) ${dartString(input.wireName)}: ${written}`;
        }
        return input.required
          ? `${dartString(input.wireName)}: ${valueExpression(input)}`
          : `${dartString(input.wireName)}: ?${writeNullable(input.type, input.name)}`;
      });
    fields.push(`body: {${members.join(", ")}}`);
  }
  fields.push("options: options");
  if (operation.concurrency) fields.push("ifMatch: ifMatch");
  if (operation.idempotent) fields.push("idempotencyKey: idempotencyKey");
  const call = `_transport.request(\n${fields.map((field) => `      ${field},\n`).join("")}    )`;
  if (item !== undefined) {
    text += `    final json = await ${call};\n`;
    text += `    return ResultPage.fromJson(readObject(json, r'$'), ${readerFunction(item)});\n  }\n`;
    return text + walkAllMethod(operation, inputs, item);
  }
  if (response === undefined) {
    text += `    await ${call};\n  }\n`;
  } else {
    text += `    final json = await ${call};\n`;
    text += `    return ${readExpression(response, "json", "r'$'")};\n  }\n`;
  }
  return text;
}

/**
 * The walk all sibling of a paged list (spec 0004 CLN-4, spec 0014 PAG-6): a lazy `Stream` of items that fetches the
 * next page only when the listener reaches it and stops when the subscription is cancelled.
 */
function walkAllMethod(operation: Operation, inputs: Input[], item: DartType): string {
  const name = methodName(operation);
  const walkInputs = inputs.filter(
    (input) => !(input.location === "query" && input.wireName === "cursor"),
  );
  let text = `\n${dartDoc(
    [
      `Walks every page of [${name}], yielding items. A page is fetched when the listener reaches it; cancelling the subscription stops fetching. [options] apply to each page's request.`,
      "Emits an [OrvanoException] when a page's call fails.",
    ],
    "  ",
  )}`;
  if (operation.deprecated)
    text += "  @Deprecated('The contract marks this operation deprecated.')\n";
  const params = [
    ...walkInputs.map(
      (input) => `${input.required ? "required " : ""}${dartTypeName(input.type)} ${input.name}`,
    ),
    "OrvanoRequestOptions? options",
  ];
  const args = [
    ...walkInputs.map((input) => `${input.name}: ${input.name}`),
    "cursor: cursor",
    "options: options",
  ];
  text += `  Stream<${dartTypeName(item)}> ${name}All({\n${params.map((param) => `    ${param},\n`).join("")}  }) => walkPages(\n    (cursor) => ${name}(${args.join(", ")}),\n  );\n`;
  return text;
}

/** A path segment from a path parameter, as a string. */
function pathValue(input: Input): string {
  const type = nonNull(input.type);
  switch (type.kind) {
    case "string":
      return input.name;
    case "enum":
      return `${input.name}.toJson()`;
    case "dateTime":
      return `writeDateTime(${input.name})`;
    default:
      return `'$${input.name}'`;
  }
}

/** A query value or a required body field, as JSON. */
function valueExpression(input: Input): string {
  return input.type.kind === "nullable"
    ? writeNullable(input.type, input.name)
    : writeExpression(input.type, input.name);
}

export function serviceFile(service: Service, types: DartTypes, source: Provenance): string {
  const body = serviceClass(service, types);
  let text = fileHeader(source);
  text += "\nimport 'package:meta/meta.dart';\n\n";
  text += "import '../core/exceptions.dart';\n";
  if (/\bread[A-Z][A-Za-z]*\(|writeDateTime\(/.test(body)) text += "import '../core/json.dart';\n";
  if (/\bOptional\b/.test(body)) text += "import '../core/optional.dart';\n";
  text += "import '../core/options.dart';\n";
  if (service.operations.some((operation) => operation.paging !== undefined))
    text += "import '../core/paging.dart';\n";
  text += "import '../core/transport.dart';\n";
  if (types.declarations().length > 0 && usesModels(body, types)) text += "import 'models.dart';\n";
  return `${text}\n${body}`;
}

function usesModels(body: string, types: DartTypes): boolean {
  return types
    .declarations()
    .some((declaration) => new RegExp(`\\b${declaration.name}\\b`).test(body));
}

// ---------------------------------------------------------------------------------------------------------------
// Constants, the services base, and the barrel

export function errorCodesFile(model: TargetModel, source: Provenance): string {
  const codes = [
    ...model.errorCodes,
    {
      code: "unknown",
      status: 0,
      description: "SDK only: the error body was not a problem body.",
      members: [],
    },
  ].toSorted((a, b) => byString(a.code, b.code));
  let text = `${fileHeader(source)}\n`;
  text += "import '../core/exceptions.dart';\n\n";
  text += dartDoc([
    "Every error code the API sends, plus `unknown`, to compare with [OrvanoApiException.code] (spec 0004 EXC-3). A newer server may send a code that is not here.",
  ]);
  text += "abstract final class OrvanoErrorCodes {\n";
  for (const entry of codes) {
    const status = entry.status === 0 ? "" : `${String(entry.status)}: `;
    text += `${dartDoc([`${status}${entry.description}`], "  ")}  static const String ${checkMemberName(camelCase(entry.code), `error code ${entry.code}`)} = ${dartString(entry.code)};\n\n`;
  }
  text = text.replace(/\n\n$/, "\n");
  return `${text}}\n`;
}

export function contractFile(model: TargetModel, source: Provenance): string {
  let text = `${fileHeader(source)}\n`;
  text += dartDoc([
    "The API version this SDK was generated for, sent as `Orvano-Api-Version` on every request (spec 0014 VRS-3).",
  ]);
  text += `const String apiVersion = ${dartString(model.version)};\n\n`;
  text += dartDoc(["Orvano's HTTP header names (spec 0014 HDR-1)."]);
  text += "abstract final class OrvanoHeaders {\n";
  const seen = new Set<string>();
  const constants = model.headers.map((header) => {
    const name = camelCase(header.name);
    if (seen.has(name) || words(header.name).length === 0) {
      throw new GeneratorError(`The header ${header.name} gives the Dart name ${name} twice.`);
    }
    seen.add(name);
    return `${dartDoc([header.description === "" ? `The \`${header.name}\` header.` : header.description], "  ")}  static const String ${name} = ${dartString(header.name)};\n`;
  });
  return `${text}${constants.join("\n")}}\n`;
}

/** The base class every client extends: one getter per service (spec 0004 CLN-2). */
export function servicesFile(model: TargetModel, source: Provenance): string {
  let text = `${fileHeader(source)}\n`;
  text += "import 'package:meta/meta.dart';\n\nimport '../core/transport.dart';\n";
  text += model.services.map((service) => `import '${serviceFileName(service)}';\n`).join("");
  text += `\n${dartDoc(["The services every Orvano client has, generated from the contract. `Orvano` and `OrvanoServer` extend it."])}`;
  text += "abstract base class OrvanoServices {\n";
  text += dartDoc(["Creates every service over [transport]."], "  ");
  const initializers = model.services.map(
    (service) => `${service.name} = ${serviceClassName(service)}(transport)`,
  );
  text += `  OrvanoServices(this.transport)${initializers.length > 0 ? `\n    : ${initializers.join(",\n      ")}` : ""};\n\n`;
  text += dartDoc(
    ["The transport the services share. Only generated code and tests use it."],
    "  ",
  );
  text += "  @internal\n  final OrvanoTransport transport;\n";
  for (const service of model.services) {
    text += `\n${dartDoc([service.description], "  ")}  final ${serviceClassName(service)} ${service.name};\n`;
  }
  return `${text}}\n`;
}

/**
 * Deviation DRT-8: the one generated barrel, with a `show` list per file, so a new service or model reaches the
 * public library without a hand edit. The public library exports it whole.
 */
export function exportsFile(model: TargetModel, types: DartTypes, source: Provenance): string {
  const lines: [string, string[]][] = [
    ...Object.entries(corePublicNames).map(([file, names]): [string, string[]] => [
      `../core/${file}`,
      [...names],
    ]),
    ["contract.dart", ["OrvanoHeaders", "apiVersion"]],
    ["error_codes.dart", ["OrvanoErrorCodes"]],
    ...(types.declarations().length > 0
      ? [
          ["models.dart", types.declarations().map((declaration) => declaration.name)] as [
            string,
            string[],
          ],
        ]
      : []),
    ...model.services.map((service): [string, string[]] => [
      serviceFileName(service),
      [serviceClassName(service)],
    ]),
  ];
  let text = `${fileHeader(source)}\n`;
  text +=
    "// Deviation DRT-8: the one generated barrel, with a show list per file, so a new service or model reaches the\n";
  text += "// public library without a hand edit. The public library exports this file whole.\n";
  for (const [file, names] of lines.toSorted((a, b) => byString(a[0], b[0]))) {
    text += `export '${file}' show ${names.toSorted(byString).join(", ")};\n`;
  }
  return text;
}
