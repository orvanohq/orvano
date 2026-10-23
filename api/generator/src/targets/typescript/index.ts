// Spec 0014 GNR-3: ts-client (sdk-js, @orvano/client) and ts-server (sdk-js-server, @orvano/server).
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { Scenario } from "../../conformance.ts";
import type { Audience, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import { scenariosJson, type OutputFile, type Ownership, type Provenance } from "../../output.ts";
import {
  checkNames,
  contractFile,
  errorCodesFile,
  indexFile,
  modelsFile,
  serviceFile,
  serviceFileName,
} from "./files.ts";
import { dispatchFile } from "./conformance.ts";
import { fileHeader } from "./render.ts";

const runtimeDirectory = fileURLToPath(new URL("../../../runtime/typescript/", import.meta.url));

export const typescriptOwnership: Ownership = {
  directories: ["src/generated", "src/runtime"],
  files: ["conformance/dispatch.generated.ts", "conformance/scenarios.generated.json"],
};

export function renderTypescript(
  model: TargetModel,
  source: Provenance,
  sdkPackage: string,
  conformance: { fixture: TargetModel; scenarios: readonly Scenario[] },
): OutputFile[] {
  checkNames(model);
  const files: OutputFile[] = [
    { path: "src/generated/contract.ts", content: contractFile(model, source, sdkPackage) },
    { path: "src/generated/error-codes.ts", content: errorCodesFile(model, source) },
    { path: "src/generated/index.ts", content: indexFile(model, source) },
    { path: "src/generated/models.ts", content: modelsFile(model, source) },
    ...model.services.map((service) => ({
      path: `src/generated/${serviceFileName(service)}`,
      content: serviceFile(service, source, model),
    })),
    ...runtimeFiles(source),
    {
      path: "conformance/dispatch.generated.ts",
      content: dispatchFile(model, conformance.fixture, source),
    },
    {
      path: "conformance/scenarios.generated.json",
      content: scenariosJson(conformance.scenarios, source),
    },
  ];
  return files.toSorted((a, b) => byString(a.path, b.path));
}

/** The shared runtime templates, copied with the generated header (spec 0005 CGN-2, spec 0014 GNR-5). */
function runtimeFiles(source: Provenance): OutputFile[] {
  return readdirSync(runtimeDirectory)
    .filter((name) => name.endsWith(".ts"))
    .toSorted(byString)
    .map((name) => ({
      path: `src/runtime/${name}`,
      content: `${fileHeader(source)}\n${readFileSync(`${runtimeDirectory}${name}`, "utf8").replace(/\r\n/g, "\n")}`,
    }));
}

export const typescriptTargets: Record<string, { audience: Audience; sdkPackage: string }> = {
  "ts-client": { audience: "client", sdkPackage: "@orvano/client" },
  "ts-server": { audience: "server", sdkPackage: "@orvano/server" },
};
