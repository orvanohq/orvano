// The files the csharp-server target writes under src/Orvano.Server/Generated/ (spec 0014 GNR-6, spec 0002 LIB-4 to
// LIB-11). The runtime they call (OrvanoTransport, OrvanoRequestOptions, OrvanoException) is hand written in
// sdk-dotnet (GNR-5); OrvanoWire.cs, copied from templates/csharp/, is the generated side of that seam.
import { GeneratorError } from "../../contract.ts";
import type { Operation, Parameter, Service, TargetModel } from "../../model.ts";
import { byString, pascalCase, words } from "../../naming.ts";
import type { OutputFile, Provenance } from "../../output.ts";
import {
  csString,
  csTypeName,
  docComment,
  docElement,
  filePrelude,
  isNullable,
  isReference,
  nonNull,
  nullable,
  parameterName,
} from "./render.ts";
import {
  requestBodyOf,
  type CSharpDeclaration,
  type CSharpType,
  type CSharpTypes,
} from "./types.ts";

export const sdkNamespace = "Orvano.Server";
export const generatedDirectory = "src/Orvano.Server/Generated";

/**
 * The `T` of every `Optional<T>` field (JSW-10), as `typeof` can name it: a reference type without its `?`, which
 * `typeof` refuses and the runtime type does not carry. Records already in the context are left out.
 */
function optionalValueTypes(types: CSharpTypes): string[] {
  const records = new Set(types.declarations().map((declaration) => declaration.name));
  const names = new Set<string>();
  for (const declaration of types.declarations()) {
    if (declaration.kind !== "class" || !declaration.partial) continue;
    for (const field of fieldsOf(declaration, types)) {
      if (!field.optional) continue;
      const type = isReference(field.type) ? nonNull(field.type) : field.type;
      if (type.kind === "class" && records.has(type.name)) continue;
      names.add(csTypeName(type));
    }
  }
  return [...names].toSorted(byString);
}

/** The item records of every paged list in a model, for the JSON context (PAG-6). */
export function pageItems(model: TargetModel, types: CSharpTypes): string[] {
  return model.services.flatMap((service) =>
    service.operations.flatMap((operation) => pageItemOf(operation, types) ?? []),
  );
}

/** Names the hand written runtime and the generated helpers already use, so no schema or service may take them. */
const takenNames: readonly string[] = [
  // the hand written runtime in sdk-dotnet (spec 0002 LIB-4 to LIB-8)
  "OrvanoClient",
  "OrvanoClientOptions",
  "OrvanoErrorKind",
  "OrvanoException",
  "OrvanoFieldError",
  "OrvanoProblem",
  "OrvanoRateLimitedException",
  "OrvanoRequestOptions",
  "OrvanoRetryOptions",
  "OrvanoServiceCollectionExtensions",
  "OrvanoTransport",
  "Optional",
  "OptionalJsonConverter",
  "ResultPage",
  // generated here
  "OrvanoCall",
  "OrvanoContract",
  "OrvanoErrorCodes",
  "OrvanoHeaders",
  "OrvanoJsonContext",
  "OrvanoQuery",
  "OrvanoServices",
  "OrvanoWire",
  // a type named like its namespace's last part hides it
  "Orvano",
  "Server",
];

/** Header constants the runtime reads, so the contract must declare them (HDR-4). */
const runtimeHeaders: readonly string[] = [
  "Idempotency-Key",
  "If-Match",
  "Orvano-Api-Version",
  "Orvano-Client",
  "Orvano-Key",
  "Orvano-Request-Id",
  "Retry-After",
];

/** Members every record or enum struct has, so no property or enum value may take them. */
const reservedMembers: ReadonlySet<string> = new Set([
  "Clone",
  "Converter",
  "Deconstruct",
  "EqualityContract",
  "Equals",
  "Finalize",
  "GetHashCode",
  "GetType",
  "IsKnown",
  "MemberwiseClone",
  "PrintMembers",
  "ReferenceEquals",
  "ToString",
  "Value",
]);

export function serviceClassName(service: Service): string {
  return `${pascalCase(service.name)}Service`;
}

export function servicePropertyName(service: Service): string {
  return pascalCase(service.name);
}

/** Fails when two generated names, or a generated and a runtime name, would be the same C# name. */
export function checkNames(model: TargetModel, types: CSharpTypes): void {
  const owners = new Map<string, string>();
  const claim = (name: string, owner: string): void => {
    const other = owners.get(name);
    if (other !== undefined)
      throw new GeneratorError(`${owner}: the C# name ${name} is also used by ${other}.`);
    owners.set(name, owner);
  };
  for (const name of takenNames) claim(name, "the runtime");
  for (const declaration of types.declarations())
    claim(declaration.name, `schema ${declaration.name}`);
  const properties = new Map<string, string>();
  for (const service of model.services) {
    claim(serviceClassName(service), `service ${service.name}`);
    const property = servicePropertyName(service);
    if (property === "Transport" || properties.has(property)) {
      throw new GeneratorError(`service ${service.name}: the client member ${property} is taken.`);
    }
    properties.set(property, service.name);
  }
  const declared = new Set(model.headers.map((header) => header.name));
  for (const header of runtimeHeaders) {
    if (!declared.has(header))
      throw new GeneratorError(`The contract does not declare the header ${header} (HDR-4).`);
  }
}

/** A C# member name from a contract name, which no record or enum struct member may already have. */
function memberName(name: string, owner: string, where: string): string {
  const member = pascalCase(name);
  if (!/^[A-Z][A-Za-z0-9]*$/.test(member))
    throw new GeneratorError(`${where}: ${name} can't be a C# member name.`);
  if (reservedMembers.has(member) || member === owner)
    throw new GeneratorError(`${where}: ${name} gives the C# member ${member}, which is taken.`);
  return member;
}

/** The usings a file body needs, read from the names it uses. */
function usingsFor(body: string): string[] {
  const usings: string[] = [];
  if (/\bCultureInfo\b/.test(body)) usings.push("System.Globalization");
  if (
    /\bJson(Element|Exception|Serializer|SerializerDefaults|SerializerOptions)\b|\bUtf8Json/.test(
      body,
    )
  )
    usings.push("System.Text.Json");
  if (/\[Json[A-Z]|\bJsonConverter\b|\bJsonSerializerContext\b|\bJsonIgnoreCondition\b/.test(body))
    usings.push("System.Text.Json.Serialization");
  if (/\bJsonTypeInfo\b/.test(body)) usings.push("System.Text.Json.Serialization.Metadata");
  if (/\bStringBuilder\b/.test(body)) usings.push("System.Text");
  if (/\[EnumeratorCancellation\]/.test(body)) usings.push("System.Runtime.CompilerServices");
  return usings;
}

/** A whole generated C# file. */
export function csFile(
  path: string,
  body: string,
  source: Provenance,
  namespace: string,
  extraUsings: readonly string[] = [],
): OutputFile {
  return {
    path,
    content: `${filePrelude(source, [...usingsFor(body), ...extraUsings], namespace)}\n${body}`,
  };
}

// ---------------------------------------------------------------------------------------------------------------
// Models

/** The file of every enum struct and record the types reach. */
export function declarationFiles(
  types: CSharpTypes,
  directory: string,
  source: Provenance,
  namespace: string,
): OutputFile[] {
  return types
    .declarations()
    .map((declaration) =>
      csFile(
        `${directory}/${declaration.name}.cs`,
        declaration.kind === "enum" ? enumBlock(declaration) : recordBlock(declaration, types),
        source,
        namespace,
      ),
    );
}

/**
 * An enum as a string backed struct (spec 0002 LIB-9): a value the SDK does not know is kept as is, so a newer
 * server never breaks an older SDK, and the value goes back on the wire unchanged.
 */
function enumBlock(declaration: Extract<CSharpDeclaration, { kind: "enum" }>): string {
  const { name } = declaration;
  const where = `Enum ${name}`;
  const members = new Map<string, string>();
  for (const value of declaration.values) {
    const member = memberName(value, name, where);
    if (members.has(member))
      throw new GeneratorError(`${where}: two values give the C# name ${member}.`);
    members.set(member, value);
  }
  let text = docComment(
    declaration.description,
    "A value a newer server sends that this SDK does not know is kept as is, with `IsKnown` false (spec 0002 LIB-9).",
    "",
  );
  text += `[JsonConverter(typeof(Converter))]\npublic readonly record struct ${name}\n{\n`;
  text += "    private readonly string? _value;\n\n";
  text += docElement("summary", "Creates a value from its wire name.", "    ");
  text += docElement("param", "The value on the wire.", "    ", ' name="value"');
  text += `    public ${name}(string value)\n    {\n        ArgumentNullException.ThrowIfNull(value);\n        _value = value;\n    }\n`;
  for (const [member, value] of members) {
    text += `\n${docElement("summary", `The \`${value}\` value.`, "    ")}`;
    text += `    public static ${name} ${member} { get; } = new(${csString(value)});\n`;
  }
  text += `\n${docElement("summary", "The value on the wire.", "    ")}`;
  text += "    public string Value => _value ?? string.Empty;\n\n";
  text += docElement("summary", "Whether this SDK knows the value.", "    ");
  const known = [...members.values()].map(csString);
  text +=
    known.length === 0
      ? "    public bool IsKnown => false;\n\n"
      : `    public bool IsKnown => Value is ${known.join(" or ")};\n\n`;
  text += docElement("summary", "The value on the wire.", "    ");
  text += "    public override string ToString() => Value;\n\n";
  text += `    internal sealed class Converter : JsonConverter<${name}>\n    {\n`;
  text += `        public override ${name} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>\n`;
  text += `            reader.TokenType == JsonTokenType.String\n                ? new(reader.GetString() ?? string.Empty)\n                : throw new JsonException(${csString(`${name} must be a string.`)});\n\n`;
  text += `        public override void Write(Utf8JsonWriter writer, ${name} value, JsonSerializerOptions options) =>\n`;
  text += "            writer.WriteStringValue(value.Value);\n    }\n}\n";
  return text;
}

/** Field names whose values `ToString` never shows (spec 0002 LIB-8). */
const secretName = /token|secret|password|^key$|apiKey/i;

interface Field {
  member: string;
  wireName: string;
  description: string;
  deprecated: boolean;
  required: boolean;
  /** A partial update's field (JSW-10): `Optional<type>`. */
  optional: boolean;
  type: CSharpType;
}

function fieldsOf(
  declaration: Extract<CSharpDeclaration, { kind: "class" }>,
  types: CSharpTypes,
): Field[] {
  const where = `Schema ${declaration.name}`;
  const seen = new Set<string>();
  return declaration.properties.map((property) => {
    const at = `${where} property ${property.name}`;
    const member = memberName(property.name, declaration.name, at);
    if (seen.has(member))
      throw new GeneratorError(`${at}: two properties give the C# name ${member}.`);
    seen.add(member);
    // JSW-10: a PATCH body's optional field is Optional<T>, nullable only when the contract's type is.
    const optional = declaration.partial && !property.required;
    return {
      member,
      wireName: property.name,
      description:
        property.description === "" ? `The \`${property.name}\` field.` : property.description,
      deprecated: property.deprecated,
      required: property.required,
      optional,
      type: optional
        ? types.resolve(
            property.type,
            `${declaration.name}${pascalCase(property.name)}`,
            true,
            at,
            property.description,
          )
        : types.property(property, declaration.name, declaration.input, at),
    };
  });
}

/**
 * A record with `init` properties (spec 0002 GEN-7). A property the contract requires is `required`, so reading a
 * body without it fails; in a request, an optional property left null is not sent.
 */
function recordBlock(
  declaration: Extract<CSharpDeclaration, { kind: "class" }>,
  types: CSharpTypes,
): string {
  const fields = fieldsOf(declaration, types);
  let text = docElement("summary", declaration.description, "");
  text += `public sealed record ${declaration.name}\n{\n`;
  text += fields
    .map((field) => {
      let block = docElement("summary", field.description, "    ");
      if (field.deprecated)
        block += '    [Obsolete("The contract marks this field deprecated.")]\n';
      block += `    [JsonPropertyName(${csString(field.wireName)})]\n`;
      if (field.optional) {
        // Absent is the default, and is not written; a set null is (JSW-10).
        block += `    [JsonConverter(typeof(OptionalJsonConverter<${csTypeName(field.type)}>))]\n`;
        block += "    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]\n";
        block += `    public Optional<${csTypeName(field.type)}> ${field.member} { get; init; }\n`;
        return block;
      }
      if (declaration.input && !field.required)
        block += "    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]\n";
      block += `    public ${field.required ? "required " : ""}${csTypeName(field.type)} ${field.member} { get; init; }\n`;
      return block;
    })
    .join("\n");
  const secrets = fields.filter((field) => secretName.test(field.wireName));
  if (secrets.length > 0) {
    text += `${fields.length > 0 ? "\n" : ""}    private bool PrintMembers(StringBuilder builder)\n    {\n`;
    text += fields
      .map((field, index) => {
        const label = `${index > 0 ? ", " : ""}${field.member} = `;
        return secrets.includes(field)
          ? `        builder.Append(${csString(`${label}<redacted>`)});\n`
          : `        builder.Append(${csString(label)}).Append(${field.member});\n`;
      })
      .join("");
    text += "        return true;\n    }\n";
  }
  return `${text}}\n`;
}

// ---------------------------------------------------------------------------------------------------------------
// Services

export interface Input {
  /** The C# parameter name, `@` escaped when needed. */
  name: string;
  wireName: string;
  description: string;
  required: boolean;
  type: CSharpType;
  location: "path" | "query" | "body";
}

/** A type a path or query parameter can hold. */
function isScalar(type: CSharpType): boolean {
  return ["string", "int", "long", "double", "bool", "guid", "dateTimeOffset", "enum"].includes(
    type.kind,
  );
}

const reservedParameters: ReadonlySet<string> = new Set([
  "request",
  "options",
  "cancellationToken",
  "call",
]);

/**
 * An operation's inputs in C# parameter order: path parameters, required query parameters, the body record, then
 * optional query parameters (spec 0002 LIB-5 puts the options and the token after them).
 */
export function inputsOf(operation: Operation, types: CSharpTypes): Input[] {
  const where = operation.operationId;
  const owner = `${pascalCase(operation.method)}${pascalCase(operation.service)}`;
  const parameter = (entry: Parameter): Input => {
    const at = `${where} parameter ${entry.name}`;
    const resolved = types.resolve(
      entry.type,
      `${owner}${pascalCase(entry.name)}`,
      true,
      at,
      entry.description,
    );
    const type = entry.required ? resolved : nullable(resolved);
    const plain = nonNull(type);
    if (
      !(
        isScalar(plain) ||
        (plain.kind === "list" && isScalar(plain.items) && entry.location === "query")
      )
    ) {
      throw new GeneratorError(
        `${where}: the ${entry.location} parameter ${entry.name} must be a string, number, boolean, uuid, timestamp, or enum${entry.location === "query" ? ", or a list of them" : ""}.`,
      );
    }
    const name = parameterName(entry.name, at);
    if (reservedParameters.has(entry.name))
      throw new GeneratorError(`${at}: ${entry.name} clashes with a parameter every method has.`);
    return {
      name,
      wireName: entry.name,
      description: entry.description === "" ? `The \`${entry.name}\` value.` : entry.description,
      required: entry.required,
      type,
      location: entry.location,
    };
  };
  const query = operation.queryParams.map(parameter);
  const body = requestBodyOf(operation);
  const bodyInput: Input[] =
    body === undefined
      ? []
      : [
          {
            name: "request",
            wireName: "",
            description: body.description,
            required: body.required,
            type: body.required
              ? { kind: "class", name: body.name }
              : { kind: "nullable", inner: { kind: "class", name: body.name } },
            location: "body",
          },
        ];
  return [
    ...operation.pathParams.map(parameter),
    ...query.filter((input) => input.required),
    ...bodyInput,
    ...query.filter((input) => !input.required),
  ];
}

/** The C# method name of an operation (LIB-5: every I/O method ends in `Async`). */
export function methodName(operation: Operation): string {
  return `${pascalCase(operation.method)}Async`;
}

/** A paged list's item record (PAG-6); its page is `ResultPage<item>`. */
export function pageItemOf(operation: Operation, types: CSharpTypes): string | undefined {
  if (operation.paging === undefined) return undefined;
  const type = types.resolve(
    operation.paging.item,
    `${pascalCase(operation.method)}${pascalCase(operation.service)}Item`,
    false,
    `${operation.operationId} item`,
  );
  if (type.kind !== "class")
    throw new GeneratorError(`${operation.operationId}: a list's items must be JSON objects.`);
  return type.name;
}

/** The JSON metadata property of `ResultPage<item>` in a JSON context. */
export function pageInfoName(item: string): string {
  return `ResultPageOf${item}`;
}

/** The response record of an operation, or undefined for 204. JSW-8: a body is always an object. */
export function responseOf(operation: Operation, types: CSharpTypes): string | undefined {
  const item = pageItemOf(operation, types);
  if (item !== undefined) return `ResultPage<${item}>`;
  if (operation.response === undefined) return undefined;
  const type = types.resolve(
    operation.response,
    `${pascalCase(operation.method)}${pascalCase(operation.service)}Response`,
    false,
    `${operation.operationId} response`,
  );
  if (type.kind !== "class")
    throw new GeneratorError(
      `${operation.operationId}: a response body must be a JSON object (JSW-8).`,
    );
  return type.name;
}

/** How a service reaches the JSON metadata of a type: the SDK's context, or the conformance one. */
export type TypeInfo = (name: string) => string;

export const sdkTypeInfo: TypeInfo = (name) => `OrvanoJsonContext.Default.${name}`;

/** A service class. */
export function serviceClass(service: Service, types: CSharpTypes, typeInfo: TypeInfo): string {
  const className = serviceClassName(service);
  let text = docElement("summary", service.description, "");
  text += `public sealed class ${className}\n{\n`;
  text += "    private readonly OrvanoTransport _transport;\n\n";
  text += `    internal ${className}(OrvanoTransport transport)\n    {\n        _transport = transport;\n    }\n`;
  for (const operation of service.operations) {
    text += `\n${method(operation, types, typeInfo)}`;
    if (operation.paging !== undefined) text += `\n${walkAllMethod(operation, types)}`;
  }
  return `${text}}\n`;
}

/**
 * The walk all sibling of a paged list (spec 0002 LIB-5, spec 0014 PAG-6): an <c>IAsyncEnumerable</c> of items that
 * fetches each page as the caller reaches it and checks the token before every page.
 */
function walkAllMethod(operation: Operation, types: CSharpTypes): string {
  const item = pageItemOf(operation, types) ?? "";
  const inputs = inputsOf(operation, types).filter(
    (input) => !(input.location === "query" && input.wireName === "cursor"),
  );
  const indent = "    ";
  let text = docComment(
    `Walks every page of \`${methodName(operation)}\`, yielding items.`,
    "Each page is fetched when the caller reaches it, so stopping the loop stops fetching; the options apply to each page's request.",
    indent,
  );
  for (const input of inputs) {
    text += docElement(
      "param",
      input.description,
      indent,
      ` name="${input.name.replace(/^@/, "")}"`,
    );
  }
  text += docElement(
    "param",
    "Per call options: the timeout, retries, and extra headers.",
    indent,
    ' name="options"',
  );
  text += docElement(
    "param",
    "Stops the walk before the next page.",
    indent,
    ' name="cancellationToken"',
  );
  text += docElement("returns", `Every \`${item}\` in the list, in its order.`, indent);
  text += docElement(
    "exception",
    "The server answered a page with an error.",
    indent,
    ' cref="OrvanoException"',
  );
  if (operation.deprecated)
    text += `${indent}[Obsolete("The contract marks this operation deprecated.")]\n`;
  const params = [
    ...inputs.map(
      (input) => `${csTypeName(input.type)} ${input.name}${input.required ? "" : " = null"}`,
    ),
    "OrvanoRequestOptions? options = null",
    "[EnumeratorCancellation] CancellationToken cancellationToken = default",
  ];
  const args = [
    ...inputs.map((input) => `${input.name}: ${input.name}`),
    "cursor: cursor",
    "options: options",
    "cancellationToken: cancellationToken",
  ];
  const name = methodName(operation).replace(/Async$/, "AllAsync");
  text += `${indent}public async IAsyncEnumerable<${item}> ${name}(${params.join(", ")})\n${indent}{\n`;
  text += `${indent}${indent}string? cursor = null;\n`;
  text += `${indent}${indent}do\n${indent}${indent}{\n`;
  text += `${indent}${indent}${indent}cancellationToken.ThrowIfCancellationRequested();\n`;
  text += `${indent}${indent}${indent}var page = await ${methodName(operation)}(${args.join(", ")}).ConfigureAwait(false);\n`;
  text += `${indent}${indent}${indent}foreach (var entry in page.Items)\n${indent}${indent}${indent}{\n`;
  text += `${indent}${indent}${indent}${indent}yield return entry;\n${indent}${indent}${indent}}\n\n`;
  text += `${indent}${indent}${indent}cursor = page.NextCursor;\n`;
  text += `${indent}${indent}}\n${indent}${indent}while (cursor is not null);\n`;
  return `${text}${indent}}\n`;
}

const httpMethods: Readonly<Record<Operation["httpMethod"], string>> = {
  DELETE: "Delete",
  GET: "Get",
  HEAD: "Head",
  OPTIONS: "Options",
  PATCH: "Patch",
  POST: "Post",
  PUT: "Put",
};

function method(operation: Operation, types: CSharpTypes, typeInfo: TypeInfo): string {
  const where = operation.operationId;
  const inputs = inputsOf(operation, types);
  const response = responseOf(operation, types);
  const item = pageItemOf(operation, types);
  // A page's JSON metadata is the context's ResultPageOf<item> entry.
  const responseInfo = item === undefined ? response : pageInfoName(item);
  const indent = "    ";

  let text = docComment(
    operation.summary === "" ? `Calls ${operation.operationId}.` : operation.summary,
    operation.description,
    indent,
  );
  for (const input of inputs) {
    text += docElement(
      "param",
      input.description,
      indent,
      ` name="${input.name.replace(/^@/, "")}"`,
    );
  }
  if (operation.concurrency) {
    text += docElement(
      "param",
      "The resource's `version` as you read it, sent as `If-Match`. The write fails with `version_conflict` when it changed since; leave it null to write anyway.",
      indent,
      ' name="ifMatch"',
    );
  }
  if (operation.idempotent) {
    text += docElement(
      "param",
      "A key you choose, 1 to 255 printable ASCII characters, sent as `Idempotency-Key`. With it, a failed call is retried; reuse it only for the same request.",
      indent,
      ' name="idempotencyKey"',
    );
  }
  text += docElement(
    "param",
    "Per call options: the timeout, retries, and extra headers.",
    indent,
    ' name="options"',
  );
  text += docElement("param", "Cancels the call.", indent, ' name="cancellationToken"');
  text += docElement(
    "returns",
    response === undefined
      ? "A task that completes when the server has answered."
      : `The \`${response}\` the server answered with.`,
    indent,
  );
  text += docElement(
    "exception",
    "The server answered with an error.",
    indent,
    ' cref="OrvanoException"',
  );
  if (operation.deprecated)
    text += `${indent}[Obsolete("The contract marks this operation deprecated.")]\n`;

  const params = [
    ...inputs.map(
      (input) => `${csTypeName(input.type)} ${input.name}${input.required ? "" : " = null"}`,
    ),
    ...(operation.concurrency ? ["string? ifMatch = null"] : []),
    ...(operation.idempotent ? ["string? idempotencyKey = null"] : []),
    "OrvanoRequestOptions? options = null",
    "CancellationToken cancellationToken = default",
  ];
  if (operation.concurrency && inputs.some((input) => input.name === "ifMatch"))
    throw new GeneratorError(
      `${where}: an input named ifMatch clashes with the ifMatch parameter.`,
    );
  if (operation.idempotent && inputs.some((input) => input.name === "idempotencyKey"))
    throw new GeneratorError(
      `${where}: an input named idempotencyKey clashes with the idempotencyKey parameter.`,
    );
  const returnType = response === undefined ? "Task" : `Task<${response}>`;
  text += `${indent}public ${returnType} ${methodName(operation)}(${params.join(", ")})\n${indent}{\n`;

  const body: string[] = [];
  for (const input of inputs) {
    // A text path value is checked where the path is built, by OrvanoWire.PathSegment.
    if (!input.required || (input.location === "path" && isTextSegment(input.type))) continue;
    if (isReference(input.type)) body.push(`ArgumentNullException.ThrowIfNull(${input.name});`);
  }

  const path = operation.segments.map((segment) => {
    switch (segment.kind) {
      case "literal":
        return csString(segment.value);
      case "project":
        return "_transport.ProjectId";
      case "param": {
        const input = inputs.find(
          (entry) => entry.location === "path" && entry.wireName === segment.name,
        );
        if (input === undefined)
          throw new GeneratorError(`${where}: the path parameter ${segment.name} is not declared.`);
        const value = wireText(input.name, input.type);
        return isTextSegment(input.type)
          ? `OrvanoWire.PathSegment(${value}, nameof(${input.name}))`
          : value;
      }
    }
  });
  const initializers: string[] = [];
  const query = inputs.filter((input) => input.location === "query");
  if (query.length > 0) {
    const entries = query.map((input) => `{ ${csString(input.wireName)}, ${queryValue(input)} }`);
    initializers.push(`Query = new OrvanoQuery { ${entries.join(", ")} },`);
  }
  const bodyInput = inputs.find((input) => input.location === "body");
  if (bodyInput !== undefined) {
    const declared = nonNull(bodyInput.type);
    const info = typeInfo(declared.kind === "class" ? declared.name : "");
    initializers.push(
      bodyInput.required
        ? `Body = OrvanoWire.Body(request, ${info}),`
        : `Body = request is null ? OrvanoWire.EmptyObject : OrvanoWire.Body(request, ${info}),`,
    );
  }
  const call = `new OrvanoCall(${csString(operation.operationId)}, HttpMethod.${httpMethods[operation.httpMethod]}, [${path.join(", ")}])`;
  body.push(
    initializers.length === 0
      ? `var call = ${call};`
      : `var call = ${call}\n{\n${initializers.map((line) => `    ${line}`).join("\n")}\n};`,
  );
  // CNC-4: the hand written runtime sends the version as a strong If-Match.
  if (operation.concurrency)
    body.push("options = OrvanoRequestOptions.WithIfMatch(options, ifMatch);");
  // IDM-1, IDM-3: the runtime sends the key and retries the call when there is one.
  if (operation.idempotent)
    body.push("options = OrvanoRequestOptions.WithIdempotencyKey(options, idempotencyKey);");
  body.push(
    response === undefined
      ? "return _transport.SendAsync(call, options, cancellationToken);"
      : `return _transport.SendAsync(call, ${typeInfo(responseInfo ?? "")}, options, cancellationToken);`,
  );
  text += body
    .map((statement) =>
      statement
        .split("\n")
        .map((line) => `${indent}${indent}${line}`)
        .join("\n"),
    )
    .join("\n");
  return `${text}\n${indent}}\n`;
}

/**
 * Whether a path value is free text, which can be empty or a dot segment (spec 0002 LIB-6, spec 0014 PTH-4); the
 * other types' wire text never is.
 */
function isTextSegment(type: CSharpType): boolean {
  const plain = nonNull(type);
  return plain.kind === "string" || plain.kind === "enum";
}

/** A path value as its wire text. */
function wireText(name: string, type: CSharpType): string {
  const plain = nonNull(type);
  if (plain.kind === "string") return name;
  if (plain.kind === "enum") return `${name}.Value`;
  return `OrvanoWire.Text(${name})`;
}

/** A query value OrvanoQuery.Add takes: a scalar, a nullable scalar, or a sequence of them. */
function queryValue(input: Input): string {
  const plain = nonNull(input.type);
  const access = isNullable(input.type) ? "?." : ".";
  if (plain.kind === "enum") return `${input.name}${access}Value`;
  if (plain.kind === "list" && plain.items.kind === "enum")
    return `${input.name}${access}Select(static item => item.Value)`;
  return input.name;
}

// ---------------------------------------------------------------------------------------------------------------
// Constants, the services base, and the JSON context

export function errorCodesBlock(model: TargetModel): string {
  const codes = [
    ...model.errorCodes,
    {
      code: "unknown",
      status: 0,
      description: "SDK only: the error body was not a problem body.",
      members: [],
    },
  ].toSorted((a, b) => byString(a.code, b.code));
  let text = docComment(
    "Every error code the API sends, plus `unknown`, to compare with `OrvanoException.Code` (spec 0002 LIB-6).",
    "A newer server may send a code that is not here.",
    "",
  );
  text += "public static class OrvanoErrorCodes\n{\n";
  const seen = new Set<string>();
  text += codes
    .map((entry) => {
      const name = pascalCase(entry.code);
      if (name === "" || seen.has(name))
        throw new GeneratorError(`The error code ${entry.code} gives the C# name ${name} twice.`);
      seen.add(name);
      const status = entry.status === 0 ? "" : `${String(entry.status)}: `;
      return `${docElement("summary", `${status}${entry.description}`, "    ")}    public const string ${name} = ${csString(entry.code)};\n`;
    })
    .join("\n");
  return `${text}}\n`;
}

export function headersBlock(model: TargetModel): string {
  let text = docElement("summary", "Orvano's HTTP header names (spec 0014 HDR-1).", "");
  text += "public static class OrvanoHeaders\n{\n";
  const seen = new Set<string>();
  text += model.headers
    .map((header) => {
      const name = pascalCase(header.name);
      if (seen.has(name) || words(header.name).length === 0)
        throw new GeneratorError(`The header ${header.name} gives the C# name ${name} twice.`);
      seen.add(name);
      const description =
        header.description === "" ? `The \`${header.name}\` header.` : header.description;
      return `${docElement("summary", description, "    ")}    public const string ${name} = ${csString(header.name)};\n`;
    })
    .join("\n");
  return `${text}}\n`;
}

export function contractBlock(model: TargetModel): string {
  let text = docElement("summary", "The API contract this SDK was generated from.", "");
  text += "public static class OrvanoContract\n{\n";
  text += docElement(
    "summary",
    "The API version this SDK was generated for, sent as `Orvano-Api-Version` on every request (spec 0014 VRS-3).",
    "    ",
  );
  text += `    public const string ApiVersion = ${csString(model.version)};\n}\n`;
  return text;
}

/** The base class the hand written `OrvanoClient` extends: one property per service (spec 0002 LIB-4). */
export function servicesBlock(model: TargetModel): string {
  let text = docComment(
    "The services every Orvano client has, generated from the contract.",
    "`OrvanoClient` extends it; nothing else can.",
    "",
  );
  text += "public abstract class OrvanoServices\n{\n";
  text += "    private protected OrvanoServices(OrvanoTransport transport)\n    {\n";
  text += "        ArgumentNullException.ThrowIfNull(transport);\n        Transport = transport;\n";
  text += model.services
    .map(
      (service) =>
        `        ${servicePropertyName(service)} = new ${serviceClassName(service)}(transport);\n`,
    )
    .join("");
  text += "    }\n\n";
  text += "    internal OrvanoTransport Transport { get; }\n";
  for (const service of model.services) {
    text += `\n${docElement("summary", service.description, "    ")}    public ${serviceClassName(service)} ${servicePropertyName(service)} { get; }\n`;
  }
  return `${text}}\n`;
}

/** A source generated JSON context for every record (spec 0002 JSN-1, LIB-9): trim and AOT safe. */
export function jsonContextBlock(
  types: CSharpTypes,
  name: string,
  description: string,
  pagedItems: readonly string[] = [],
): string {
  const records = types.declarations().filter((declaration) => declaration.kind === "class");
  let text = docElement("summary", description, "");
  text +=
    "[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]\n";
  text += records
    .map((declaration) => `[JsonSerializable(typeof(${declaration.name}))]\n`)
    .join("");
  // JSW-10: OptionalJsonConverter<T> writes T through this context, so it needs T's metadata.
  text += optionalValueTypes(types)
    .map((type) => `[JsonSerializable(typeof(${type}))]\n`)
    .join("");
  text += [...new Set(pagedItems)]
    .toSorted(byString)
    .map(
      (item) =>
        `[JsonSerializable(typeof(ResultPage<${item}>), TypeInfoPropertyName = "${pageInfoName(item)}")]\n`,
    )
    .join("");
  return `${text}internal sealed partial class ${name} : JsonSerializerContext;\n`;
}
