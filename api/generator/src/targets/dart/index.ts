// Spec 0014 GNR-3: dart-client (sdk-flutter, packages/orvano) and dart-server (sdk-dart, orvano_server), both written
// relative to the package root (spec 0004 PKG-1 to PKG-3).
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { Scenario } from "../../conformance.ts";
import type { Audience, TargetModel } from "../../model.ts";
import { byString } from "../../naming.ts";
import { scenariosJson, type OutputFile, type Ownership, type Provenance } from "../../output.ts";
import { dispatchFile } from "./conformance.ts";
import {
  checkNames,
  contractFile,
  errorCodesFile,
  exportsFile,
  modelsFile,
  serviceFile,
  serviceFileName,
  servicesFile,
} from "./files.ts";
import { fileHeader } from "./render.ts";
import { DartTypes } from "./types.ts";

const runtimeDirectory = fileURLToPath(new URL("../../../runtime/dart/", import.meta.url));

export const dartOwnership: Ownership = {
  directories: ["lib/src/generated", "lib/src/core"],
  files: ["test/conformance/dispatch.g.dart", "test/conformance/scenarios.g.json"],
};

export function renderDart(
  model: TargetModel,
  source: Provenance,
  dartPackage: string,
  conformance: { fixture: TargetModel; scenarios: readonly Scenario[] },
): OutputFile[] {
  const types = new DartTypes(model);
  // Services first: their inline input types become declarations the models file then emits.
  const services = model.services.map((service) => ({
    path: `lib/src/generated/${serviceFileName(service)}`,
    content: serviceFile(service, types, source),
  }));
  const dispatch = dispatchFile(model, types, conformance.fixture, dartPackage, source);
  checkNames(model, types);
  const files: OutputFile[] = [
    { path: "lib/src/generated/contract.dart", content: contractFile(model, source) },
    { path: "lib/src/generated/error_codes.dart", content: errorCodesFile(model, source) },
    { path: "lib/src/generated/exports.dart", content: exportsFile(model, types, source) },
    { path: "lib/src/generated/models.dart", content: modelsFile(types, source) },
    { path: "lib/src/generated/services.dart", content: servicesFile(model, source) },
    ...services,
    ...runtimeFiles(source),
    { path: "test/conformance/dispatch.g.dart", content: dispatch },
    {
      path: "test/conformance/scenarios.g.json",
      content: scenariosJson(conformance.scenarios, source),
    },
  ];
  return files.toSorted((a, b) => byString(a.path, b.path));
}

/** The shared runtime templates, copied with the generated header (spec 0004 PKG-3, spec 0014 GNR-5). */
function runtimeFiles(source: Provenance): OutputFile[] {
  return readdirSync(runtimeDirectory)
    .filter((name) => name.endsWith(".dart"))
    .toSorted(byString)
    .map((name) => ({
      path: `lib/src/core/${name}`,
      content: `${fileHeader(source)}\n${readFileSync(`${runtimeDirectory}${name}`, "utf8").replace(/\r\n/g, "\n")}`,
    }));
}

export const dartTargets: Record<string, { audience: Audience; dartPackage: string }> = {
  "dart-client": { audience: "client", dartPackage: "orvano" },
  "dart-server": { audience: "server", dartPackage: "orvano_server" },
};
