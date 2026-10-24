// Spec 0014 CNF-3, CNF-4, STB-4: the C# conformance dispatcher, written into tests/Orvano.Server.ConformanceTests/
// Generated/. It maps each scenario call to the real SDK method (through the client's service properties) and
// normalizes errors; the matcher and substitution come from templates/csharp/ConformanceSupport.cs.tmpl, so every
// runner matches the same way. Fixture services render here alone, never into src/.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { GeneratorError } from "../../contract.ts";
import type { Operation, Service, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import type { OutputFile, Provenance } from "../../output.ts";
import {
  csFile,
  declarationFiles,
  inputsOf,
  jsonContextBlock,
  methodName,
  pageItems,
  responseOf,
  sdkNamespace,
  serviceClass,
  serviceClassName,
  servicePropertyName,
} from "./files.ts";
import { csString, csTypeName, fileHeader, nonNull } from "./render.ts";
import { CSharpTypes } from "./types.ts";

export const conformanceDirectory = "tests/Orvano.Server.ConformanceTests/Generated";

/** The fixture's records are read through the conformance project's own JSON context. */
function fixtureInfo(name: string): string {
  return `ConformanceJsonContext.Default.${name}`;
}
const conformanceNamespace = "Orvano.Server.ConformanceTests";

export function conformanceFiles(
  model: TargetModel,
  types: CSharpTypes,
  fixture: TargetModel,
  source: Provenance,
): OutputFile[] {
  const fixtureTypes = new CSharpTypes(fixture);
  const services = fixture.services.map((service) =>
    csFile(
      `${conformanceDirectory}/Fixture/${serviceClassName(service)}.cs`,
      serviceClass(service, fixtureTypes, fixtureInfo),
      source,
      conformanceNamespace,
      [sdkNamespace],
    ),
  );
  const models = declarationFiles(
    fixtureTypes,
    `${conformanceDirectory}/Fixture`,
    source,
    conformanceNamespace,
  );
  const contractNames = new Set([
    ...types.declarations().map((declaration) => declaration.name),
    ...model.services.map(serviceClassName),
  ]);
  for (const name of [
    ...fixtureTypes.declarations().map((declaration) => declaration.name),
    ...fixture.services.map(serviceClassName),
  ]) {
    if (contractNames.has(name))
      throw new GeneratorError(`The fixture's C# name ${name} is also a contract name.`);
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

  let dispatch = "/// <summary>The conformance dispatcher (spec 0014 CNF-4).</summary>\n";
  dispatch += "internal static class ConformanceDispatcher\n{\n";
  dispatch +=
    "    /// <summary>Every call this SDK's scenarios may make, as <c>&lt;service&gt;.&lt;method&gt;</c>.</summary>\n";
  dispatch += `    public static IReadOnlyList<string> Calls { get; } =\n    [\n${entries.map(({ operation }) => `        ${csString(callName(operation))},\n`).join("")}    ];\n\n`;
  dispatch +=
    "    /// <summary>Runs one scenario step's call and returns its result as JSON, or the normalized error.</summary>\n";
  dispatch +=
    "    public static async Task<ConformanceOutcome> DispatchAsync(OrvanoServices client, string call, JsonObject parameters, ConformanceOptions options, CancellationToken cancellationToken)\n    {\n";
  dispatch += "        var requestOptions = options.ToRequestOptions();\n";
  const pagedCalls = entries
    .filter(({ operation }) => operation.paging !== undefined)
    .map(({ operation }) => csString(callName(operation)));
  dispatch += `        if (options.All && ${pagedCalls.length === 0 ? "true" : `call is not (${pagedCalls.join(" or ")})`})\n        {\n            throw new ArgumentException($"{call} is not a paged list, so it has no walk all method.", nameof(call));\n        }\n\n`;
  dispatch += "        try\n        {\n            switch (call)\n            {\n";
  dispatch += entries
    .map((entry) => dispatchCase(entry.service, entry.operation, entry.fixture, entry.types))
    .join("");
  dispatch += `                default:\n                    throw new ArgumentException($"{call} is not a call this SDK has.", nameof(call));\n            }\n        }\n`;
  dispatch +=
    "        catch (Exception error) when (ConformanceErrors.Normalize(error) is { } normalized)\n        {\n            return ConformanceOutcome.Failed(normalized);\n        }\n    }\n}\n";

  return [
    csFile(
      `${conformanceDirectory}/ConformanceDispatcher.cs`,
      dispatch,
      source,
      conformanceNamespace,
      ["System.Text.Json.Nodes", sdkNamespace],
    ),
    csFile(
      `${conformanceDirectory}/ConformanceJsonContext.cs`,
      jsonContextBlock(
        fixtureTypes,
        "ConformanceJsonContext",
        "JSON metadata for the fixture's records (spec 0014 STB-4).",
        pageItems(fixture, fixtureTypes),
      ),
      source,
      conformanceNamespace,
    ),
    {
      path: `${conformanceDirectory}/ConformanceSupport.cs`,
      content: `${fileHeader(source)}${staticPart}`,
    },
    ...models,
    ...services,
  ];
}

function callName(operation: Operation): string {
  return `${operation.service}.${operation.method}`;
}

function dispatchCase(
  service: Service,
  operation: Operation,
  fixture: boolean,
  types: CSharpTypes,
): string {
  const inputs = inputsOf(operation, types);
  const args = [
    ...inputs.map((input) => {
      if (input.location === "body") {
        const type = nonNull(input.type);
        return `ConformanceJson.Body<${type.kind === "class" ? type.name : "object"}>(parameters)`;
      }
      return input.required
        ? `ConformanceJson.Required<${csTypeName(input.type)}>(parameters, ${csString(input.wireName)})`
        : `ConformanceJson.Optional<${csTypeName(input.type)}>(parameters, ${csString(input.wireName)})`;
    }),
    "requestOptions",
    "cancellationToken",
  ];
  const target = fixture
    ? `new ${serviceClassName(service)}(client.Transport)`
    : `client.${servicePropertyName(service)}`;
  const invocation = `${target}.${methodName(operation)}(${args.join(", ")}).ConfigureAwait(false)`;
  const name = csString(callName(operation));
  const indent = "                ";
  if (operation.paging !== undefined) {
    // CNF-2 `all`: the walk all sibling, its items collected (PAG-6).
    const cursor = inputs.findIndex(
      (input) => input.location === "query" && input.wireName === "cursor",
    );
    // The walk takes the same inputs without the cursor, and the walk's own token in place of the step's.
    const walkArgs = [...args.slice(0, -1).filter((_, index) => index !== cursor), "token"];
    const walkMethod = methodName(operation).replace(/Async$/, "AllAsync");
    let text = `${indent}case ${name}:\n${indent}    if (options.All)\n${indent}    {\n`;
    text += `${indent}        return await ConformanceWalk.AllAsync(token => ${target}.${walkMethod}(${walkArgs.join(", ")}), options.CancelAfter, cancellationToken).ConfigureAwait(false);\n${indent}    }\n\n`;
    text += `${indent}    {\n${indent}        var result = await ${invocation};\n${indent}        return ConformanceOutcome.Ok(ConformanceJson.ToNode(result));\n${indent}    }\n`;
    return text;
  }
  if (responseOf(operation, types) === undefined) {
    return `${indent}case ${name}:\n${indent}    await ${invocation};\n${indent}    return ConformanceOutcome.Ok(null);\n`;
  }
  return `${indent}case ${name}:\n${indent}    {\n${indent}        var result = await ${invocation};\n${indent}        return ConformanceOutcome.Ok(ConformanceJson.ToNode(result));\n${indent}    }\n`;
}

// Written once and copied as is: the options, outcome, error, and matching rules every C# runner shares.
const staticPart = readFileSync(
  fileURLToPath(new URL("../../../templates/csharp/ConformanceSupport.cs.tmpl", import.meta.url)),
  "utf8",
)
  .replace(/\r\n/g, "\n")
  .replace(/^\/\/.*\n/, "");
