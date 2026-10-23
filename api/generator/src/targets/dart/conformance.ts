// Spec 0014 CNF-3, CNF-4, STB-4: the Dart conformance dispatcher. It maps each scenario call to the real SDK method
// (through the client's service getters) and normalizes errors; the matcher and substitution are generated here too,
// so both Dart repos match the same way. Fixture services render into this file alone, never into lib/.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { GeneratorError } from "../../contract.ts";
import type { Operation, Service, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import type { Provenance } from "../../output.ts";
import {
  declarationBlocks,
  inputsOf,
  methodName,
  serviceClass,
  serviceClassName,
} from "./files.ts";
import { dartString, fileHeader, readExpression, writeExpression } from "./render.ts";
import { DartTypes } from "./types.ts";

export function dispatchFile(
  model: TargetModel,
  types: DartTypes,
  fixture: TargetModel,
  dartPackage: string,
  source: Provenance,
): string {
  const fixtureTypes = new DartTypes(fixture);
  const fixtureServices = fixture.services
    .map((service) => serviceClass(service, fixtureTypes))
    .join("\n");
  const fixtureModels = declarationBlocks(fixtureTypes);
  const contractNames = new Set([
    ...types.declarations().map((declaration) => declaration.name),
    ...model.services.map(serviceClassName),
  ]);
  for (const name of [
    ...fixtureTypes.declarations().map((declaration) => declaration.name),
    ...fixture.services.map(serviceClassName),
  ]) {
    if (contractNames.has(name))
      throw new GeneratorError(`The fixture's Dart name ${name} is also a contract name.`);
  }

  const entries = [
    ...model.services.flatMap((service) =>
      service.operations.map((operation) => ({ service, operation, fixture: false, types })),
    ),
    ...fixture.services.flatMap((service) =>
      service.operations.map((operation) => ({
        service,
        operation,
        fixture: true,
        types: fixtureTypes,
      })),
    ),
  ].toSorted((a, b) => byString(callName(a.operation), callName(b.operation)));
  const cases = entries
    .map((entry) => dispatchCase(entry.service, entry.operation, entry.fixture, entry.types))
    .join("");

  const library = `package:${dartPackage}/src`;
  const imports = new Set([
    "dart:convert",
    "package:http/http.dart",
    `${library}/core/exceptions.dart`,
    `${library}/core/json.dart`,
    `${library}/core/options.dart`,
    `${library}/generated/error_codes.dart`,
    `${library}/generated/services.dart`,
  ]);
  if (fixture.services.length > 0) {
    imports.add("package:meta/meta.dart");
    imports.add(`${library}/core/transport.dart`);
  }
  if (fixtureModels.includes("_equality")) imports.add("package:collection/collection.dart");
  const usesContractModels = types
    .declarations()
    .some((declaration) => new RegExp(`\\b${declaration.name}\\b`).test(cases));
  if (usesContractModels) imports.add(`${library}/generated/models.dart`);

  const sortedImports = [...imports].toSorted(byString);
  const dart = sortedImports.filter((path) => path.startsWith("dart:"));
  const packages = sortedImports.filter(
    (path) => path.startsWith("package:") && !path.startsWith(`package:${dartPackage}/`),
  );
  const own = sortedImports.filter((path) => path.startsWith(`package:${dartPackage}/`));

  let text = fileHeader(source);
  text +=
    "// The conformance dispatcher (spec 0014 CNF-4) and matcher (CNF-3). The fixture services below exist only in\n";
  text += "// the stub server (STB-4) and never ship in the SDK.\n";
  text += "\n";
  text += dart.map((path) => `import '${path}';\n`).join("");
  text += `\n${packages
    .map((path) =>
      path === "package:http/http.dart" ? `import '${path}' as http;\n` : `import '${path}';\n`,
    )
    .join("")}`;
  text += `\n${own.map((path) => `import '${path}';\n`).join("")}`;
  if (fixtureModels.includes("_equality")) {
    text += "\nconst DeepCollectionEquality _equality = DeepCollectionEquality();\n";
  }
  if (fixtureModels !== "") text += `\n${fixtureModels}`;
  if (fixtureServices !== "") text += `\n${fixtureServices}`;
  text += staticPart;
  text += "\n/// Every call this SDK's scenarios may make, as `<service>.<method>`.\n";
  text += `const List<String> calls = [\n${entries.map(({ operation }) => `  ${dartString(callName(operation))},\n`).join("")}];\n`;
  text +=
    "\n/// Runs one scenario step's call and returns its result as JSON, or the normalized error (CNF-4).\n";
  text +=
    "Future<ConformanceOutcome> dispatch(\n  OrvanoServices client,\n  String call,\n  Map<String, Object?> params,\n  ConformanceOptions options,\n) async {\n";
  text += "  final requestOptions = options.toRequestOptions();\n  try {\n    switch (call) {\n";
  text += cases;
  text +=
    "      default:\n        throw ArgumentError.value(call, 'call', 'is not a call this SDK has');\n    }\n";
  text +=
    "  } on OrvanoException catch (error) {\n    return ConformanceOutcome.failed(normalize(error));\n  }\n}\n";
  return text;
}

function callName(operation: Operation): string {
  return `${operation.service}.${operation.method}`;
}

function dispatchCase(
  service: Service,
  operation: Operation,
  fixture: boolean,
  types: DartTypes,
): string {
  const inputs = inputsOf(operation, types);
  const args = [
    ...inputs.map(
      (input) =>
        `${input.name}: ${readExpression(input.type, `params[${dartString(input.wireName)}]`, dartString(`$.${input.wireName}`))}`,
    ),
    "options: requestOptions",
  ];
  const target = fixture
    ? `${serviceClassName(service)}(client.transport)`
    : `client.${service.name}`;
  const invocation = `${target}.${methodName(operation)}(\n${args.map((arg) => `          ${arg},\n`).join("")}        )`;
  const name = dartString(callName(operation));
  if (operation.response === undefined) {
    return `      case ${name}:\n        await ${invocation};\n        return const ConformanceOutcome.ok(null);\n`;
  }
  const response = types.resolve(operation.response, "", true, `${operation.operationId} response`);
  return `      case ${name}:\n        final result = await ${invocation};\n        return ConformanceOutcome.ok(${writeExpression(response, "result")});\n`;
}

// Written once and copied as is: the options, outcome, error, and matching rules every Dart runner shares.
const staticPart = `\n${readFileSync(
  fileURLToPath(new URL("../../../templates/dart/dispatch_support.dart.tmpl", import.meta.url)),
  "utf8",
)
  .replace(/\r\n/g, "\n")
  .replace(/^\/\/.*\n/, "")}`;
