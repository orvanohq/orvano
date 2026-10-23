// Spec 0014 CNF-1, CNF-9: the conformance scenarios in core CI.
//   node tooling/scripts/api-conformance.mjs write   (pnpm api:scenarios) bundles scenarios/*.yaml into scenarios.json
//   node tooling/scripts/api-conformance.mjs check   (pnpm api:check) fails on any of:
//     a scenario file that does not validate against scenario.schema.json, a duplicate scenario ID,
//     a call to an operation that does not exist for its mode, scenarios.json out of date with the YAML,
//     or a public operation with no live scenario and no entry in pending.json.
// The generator reads scenarios.json, so it needs no YAML parser (GNR-1).
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { Ajv2020 } from "ajv/dist/2020.js";
import { parse } from "yaml";

const root = fileURLToPath(new URL("../..", import.meta.url));
const conformance = join(root, "api", "conformance");
const methods = ["delete", "get", "head", "options", "patch", "post", "put"];

/**
 * @typedef {{ id: string, mode: "live" | "stub", audience: string[], steps: { call: string }[] }} Scenario
 * @typedef {{ operationId: string, reason: string, issue: string }} Pending
 * @typedef {{ operationId: string, call: string }} PublicOperation
 */

/**
 * Reads and validates every scenario file, returning the scenarios sorted by ID and the problems found.
 * @param {string} directory api/conformance
 * @returns {{ scenarios: Scenario[], problems: string[] }}
 */
export function loadScenarios(directory) {
  const schema = /** @type {object} */ (
    JSON.parse(readFileSync(join(directory, "scenario.schema.json"), "utf8"))
  );
  const validate = new Ajv2020({ allErrors: true, strict: true, strictRequired: false }).compile(
    schema,
  );
  /** @type {string[]} */
  const problems = [];
  /** @type {Scenario[]} */
  const scenarios = [];
  const files = readdirSync(join(directory, "scenarios"))
    .filter((name) => name.endsWith(".yaml"))
    .toSorted();
  for (const name of files) {
    /** @type {unknown} */
    let document;
    try {
      document = parse(readFileSync(join(directory, "scenarios", name), "utf8"), {
        strict: true,
        uniqueKeys: true,
      });
    } catch (error) {
      problems.push(`${name}: ${error instanceof Error ? error.message : String(error)}`);
      continue;
    }
    if (!validate(document)) {
      for (const error of validate.errors ?? [])
        problems.push(`${name}${error.instancePath}: ${error.message ?? "is not valid"}`);
      continue;
    }
    scenarios.push(.../** @type {{ scenarios: Scenario[] }} */ (document).scenarios);
  }
  const seen = new Set();
  for (const scenario of scenarios) {
    if (seen.has(scenario.id)) problems.push(`Scenario ${scenario.id} is defined twice.`);
    seen.add(scenario.id);
  }
  return {
    scenarios: scenarios.toSorted((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)),
    problems,
  };
}

/**
 * Every operation in a contract with its `<service>.<method>` call name.
 * @param {string} path
 * @returns {PublicOperation[]}
 */
export function operationsOf(path) {
  const document =
    /** @type {{ paths?: Record<string, Record<string, { operationId?: string, "x-orvano-sdk"?: { service: string, method: string } }>> }} */ (
      JSON.parse(readFileSync(path, "utf8"))
    );
  return Object.values(document.paths ?? {}).flatMap((item) =>
    methods.flatMap((method) => {
      const operation = item[method];
      const sdk = operation?.["x-orvano-sdk"];
      if (operation === undefined || sdk === undefined) return [];
      return [{ operationId: operation.operationId ?? "", call: `${sdk.service}.${sdk.method}` }];
    }),
  );
}

/**
 * CNF-9 and the call check: what live and stub scenarios may call, and which public operations lack a live one.
 * @param {PublicOperation[]} publicOperations
 * @param {PublicOperation[]} fixtureOperations
 * @param {Scenario[]} scenarios
 * @param {Pending[]} pending
 * @returns {string[]}
 */
export function checkCoverage(publicOperations, fixtureOperations, scenarios, pending) {
  /** @type {string[]} */
  const problems = [];
  const live = new Set(publicOperations.map((operation) => operation.call));
  const stub = new Set([...live, ...fixtureOperations.map((operation) => operation.call)]);
  const called = new Set();
  for (const scenario of scenarios) {
    for (const step of scenario.steps) {
      const known = scenario.mode === "live" ? live : stub;
      if (!known.has(step.call))
        problems.push(
          `Scenario ${scenario.id} calls ${step.call}, which no ${scenario.mode} operation has.`,
        );
      if (scenario.mode === "live") called.add(step.call);
    }
  }
  const pendingIds = new Set(pending.map((entry) => entry.operationId));
  for (const entry of pending) {
    const operation = publicOperations.find(
      (candidate) => candidate.operationId === entry.operationId,
    );
    if (operation === undefined)
      problems.push(`pending.json lists ${entry.operationId}, which is not a public operation.`);
    else if (called.has(operation.call))
      problems.push(
        `pending.json lists ${entry.operationId}, which now has a live scenario; remove it.`,
      );
    if (
      typeof entry.reason !== "string" ||
      entry.reason === "" ||
      !/^https:\/\/github\.com\/orvanohq\/[^/]+\/issues\/\d+$/.test(entry.issue)
    ) {
      problems.push(
        `pending.json: ${entry.operationId} needs a reason and an orvanohq issue link.`,
      );
    }
  }
  for (const operation of publicOperations) {
    if (!called.has(operation.call) && !pendingIds.has(operation.operationId)) {
      problems.push(
        `${operation.operationId} (${operation.call}) has no live scenario. Add one, or list it in pending.json with an issue.`,
      );
    }
  }
  return problems;
}

/** @param {Scenario[]} scenarios */
export function bundle(scenarios) {
  return `${JSON.stringify({ scenarios }, null, 2)}\n`;
}

function main() {
  const mode = process.argv[2];
  if (mode !== "write" && mode !== "check") {
    console.error("Usage: api-conformance.mjs write|check");
    process.exit(2);
  }
  const { scenarios, problems } = loadScenarios(conformance);
  const bundlePath = join(conformance, "scenarios.json");
  if (mode === "write" && problems.length === 0) writeFileSync(bundlePath, bundle(scenarios));
  if (
    mode === "check" &&
    problems.length === 0 &&
    readFileSync(bundlePath, "utf8") !== bundle(scenarios)
  ) {
    problems.push(
      "api/conformance/scenarios.json is out of date with scenarios/*.yaml. Run pnpm api:scenarios.",
    );
  }
  const pending = /** @type {Pending[]} */ (
    JSON.parse(readFileSync(join(conformance, "pending.json"), "utf8"))
  );
  problems.push(
    ...checkCoverage(
      operationsOf(join(root, "api", "openapi.json")),
      operationsOf(join(conformance, "fixture.openapi.json")),
      scenarios,
      pending,
    ),
  );
  if (problems.length > 0) {
    for (const problem of problems) console.error(`api-conformance: ${problem}`);
    process.exit(1);
  }
  console.log(
    `api-conformance: ${String(scenarios.length)} scenarios are valid and every public operation is covered.`,
  );
}

if (process.argv[1] === fileURLToPath(import.meta.url)) main();
