// Spec 0014 GNR-3: csharp-server (sdk-dotnet, Orvano.Server), written relative to the repo root. The runtime the
// generated code calls (transport, retry, errors) is hand written in sdk-dotnet beside Generated/ (GNR-5).
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { Scenario } from "../../conformance.ts";
import type { Audience, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import { scenariosJson, type OutputFile, type Ownership, type Provenance } from "../../output.ts";
import { conformanceDirectory, conformanceFiles } from "./conformance.ts";
import {
  checkNames,
  contractBlock,
  csFile,
  declarationFiles,
  errorCodesBlock,
  generatedDirectory,
  headersBlock,
  jsonContextBlock,
  sdkNamespace,
  sdkTypeInfo,
  serviceClass,
  serviceClassName,
  servicesBlock,
} from "./files.ts";
import { fileHeader } from "./render.ts";
import { CSharpTypes } from "./types.ts";

export const csharpOwnership: Ownership = {
  directories: [generatedDirectory, conformanceDirectory],
  files: [],
};

export function renderCsharp(
  model: TargetModel,
  source: Provenance,
  conformance: { fixture: TargetModel; scenarios: readonly Scenario[] },
): OutputFile[] {
  const types = new CSharpTypes(model);
  // Services first: their inline input and response types become declarations the model files then emit.
  const services = model.services.map((service) =>
    csFile(
      `${generatedDirectory}/Services/${serviceClassName(service)}.cs`,
      serviceClass(service, types, sdkTypeInfo),
      source,
      sdkNamespace,
    ),
  );
  const conformanceOutput = conformanceFiles(model, types, conformance.fixture, source);
  checkNames(model, types);
  const files: OutputFile[] = [
    csFile(`${generatedDirectory}/OrvanoContract.cs`, contractBlock(model), source, sdkNamespace),
    csFile(
      `${generatedDirectory}/OrvanoErrorCodes.cs`,
      errorCodesBlock(model),
      source,
      sdkNamespace,
    ),
    csFile(`${generatedDirectory}/OrvanoHeaders.cs`, headersBlock(model), source, sdkNamespace),
    csFile(
      `${generatedDirectory}/OrvanoJsonContext.cs`,
      jsonContextBlock(
        types,
        "OrvanoJsonContext",
        "JSON metadata for every record (spec 0002 JSN-1, LIB-9): trim and AOT safe.",
      ),
      source,
      sdkNamespace,
    ),
    csFile(`${generatedDirectory}/OrvanoServices.cs`, servicesBlock(model), source, sdkNamespace),
    {
      path: `${generatedDirectory}/OrvanoWire.cs`,
      content: `${fileHeader(source)}${wireTemplate}`,
    },
    ...declarationFiles(types, `${generatedDirectory}/Models`, source, sdkNamespace),
    ...services,
    ...conformanceOutput,
    {
      path: `${conformanceDirectory}/scenarios.json`,
      content: scenariosJson(conformance.scenarios, source),
    },
  ];
  return files.toSorted((a, b) => byString(a.path, b.path));
}

// Written once and copied as is: OrvanoCall, OrvanoQuery, and OrvanoWire.
const wireTemplate = readFileSync(
  fileURLToPath(new URL("../../../templates/csharp/OrvanoWire.cs.tmpl", import.meta.url)),
  "utf8",
)
  .replace(/\r\n/g, "\n")
  .replace(/^\/\/.*\n/, "");

export const csharpTargets: Record<string, { audience: Audience }> = {
  "csharp-server": { audience: "server" },
};
