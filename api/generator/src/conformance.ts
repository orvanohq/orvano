// Spec 0014 CNF-4, STB-4: what the generator reads from api/conformance/. Scenarios are written in YAML by people;
// `pnpm api:scenarios` bundles them into scenarios.json (validated against scenario.schema.json), so the generator
// needs no YAML parser and keeps no runtime dependencies (GNR-1).
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { GeneratorError, loadContract, type LoadedContract } from "./contract.ts";
import type { Audience, Model } from "./model.ts";
import { byString } from "./naming.ts";

export interface ScenarioStep {
  call: string;
  params?: Record<string, unknown>;
  options?: Record<string, unknown>;
  expect: Record<string, unknown>;
  save?: Record<string, string>;
}

export interface Scenario {
  id: string;
  title: string;
  mode: "live" | "stub";
  audience: Audience[];
  requires: string[];
  client?: Record<string, unknown>;
  steps: ScenarioStep[];
  stub?: unknown[];
  expectRequests?: unknown[];
}

export interface ConformanceInputs {
  scenarios: Scenario[];
  /** Stub only operations, rendered into the dispatcher alone (STB-4). */
  fixture: LoadedContract;
}

export function loadConformance(directory: string): ConformanceInputs {
  const scenariosPath = join(directory, "scenarios.json");
  const fixturePath = join(directory, "fixture.openapi.json");
  for (const path of [scenariosPath, fixturePath]) {
    if (!existsSync(path)) throw new GeneratorError(`The conformance file ${path} is missing.`);
  }
  const bundle = JSON.parse(readFileSync(scenariosPath, "utf8")) as { scenarios?: Scenario[] };
  if (!Array.isArray(bundle.scenarios))
    throw new GeneratorError(`${scenariosPath} has no scenarios array.`);
  return { scenarios: bundle.scenarios, fixture: loadContract(fixturePath) };
}

/**
 * The scenarios one audience runs, sorted by ID. Fails naming the scenario when a step calls something neither the
 * contract nor the fixture has for that audience, or a live scenario calls a fixture operation.
 */
export function scenariosFor(
  scenarios: readonly Scenario[],
  audience: Audience,
  model: Model,
  fixture: Model,
): Scenario[] {
  const calls = (source: Model): Set<string> =>
    new Set(
      source.services.flatMap((service) =>
        service.operations
          .filter((operation) => audience === "server" || operation.audience.includes("client"))
          .map((operation) => `${operation.service}.${operation.method}`),
      ),
    );
  const live = calls(model);
  const stub = new Set([...live, ...calls(fixture)]);
  const selected = scenarios.filter((scenario) => scenario.audience.includes(audience));
  for (const scenario of selected) {
    for (const step of scenario.steps) {
      const known = scenario.mode === "live" ? live : stub;
      if (!known.has(step.call)) {
        throw new GeneratorError(
          `Scenario ${scenario.id}: no ${audience} operation ${step.call} for a ${scenario.mode} scenario.`,
        );
      }
    }
  }
  return selected.toSorted((a, b) => byString(a.id, b.id));
}
