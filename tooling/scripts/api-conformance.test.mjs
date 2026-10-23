// Spec 0014 CNF-1, CNF-9: pnpm api:check validates scenarios and fails on a public operation with no live scenario.
import assert from "node:assert/strict";
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import { bundle, checkCoverage, loadScenarios, operationsOf } from "./api-conformance.mjs";

const conformance = fileURLToPath(new URL("../../api/conformance", import.meta.url));
const publicOps = [{ operationId: "Host_Health", call: "health.get" }];
const fixtureOps = [{ operationId: "Fixture_GetWidget", call: "fixture.getWidget" }];

/**
 * @param {"live" | "stub"} mode
 * @param {string} call
 * @returns {{ id: string, mode: "live" | "stub", audience: string[], steps: { call: string }[] }}
 */
const scenario = (mode, call) => ({
  id: `a.b.${mode}`,
  mode,
  audience: ["client"],
  steps: [{ call }],
});

/** Copies api/conformance to a temp folder, writes one scenario file into it, and loads it. @param {string} yaml */
function loadWith(yaml) {
  const directory = mkdtempSync(join(tmpdir(), "orvano-scenarios-"));
  try {
    cpSync(conformance, directory, { recursive: true });
    writeFileSync(join(directory, "scenarios", "zz-test.yaml"), yaml);
    return loadScenarios(directory);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

describe("0014 AC-17: pnpm api:check", () => {
  it("passes on the committed scenarios, and scenarios.json matches them", () => {
    const { scenarios, problems } = loadScenarios(conformance);
    assert.deepEqual(problems, []);
    assert.equal(readFileSync(join(conformance, "scenarios.json"), "utf8"), bundle(scenarios));
    const coverage = checkCoverage(
      operationsOf(join(conformance, "..", "openapi.json")),
      operationsOf(join(conformance, "fixture.openapi.json")),
      scenarios,
      JSON.parse(readFileSync(join(conformance, "pending.json"), "utf8")),
    );
    assert.deepEqual(coverage, []);
  });

  it("0014 CNF-9: fails a public operation with no live scenario, unless pending.json lists it", () => {
    const stubOnly = [scenario("stub", "health.get")];
    assert.match(
      checkCoverage(publicOps, fixtureOps, stubOnly, []).join("\n"),
      /Host_Health \(health\.get\) has no live scenario/,
    );
    const pending = [
      {
        operationId: "Host_Health",
        reason: "Needs a project.",
        issue: "https://github.com/orvanohq/orvano/issues/1",
      },
    ];
    assert.deepEqual(checkCoverage(publicOps, fixtureOps, stubOnly, pending), []);
  });

  it("fails a stale or unexplained pending entry", () => {
    const covered = [scenario("live", "health.get")];
    const stale = [
      {
        operationId: "Host_Health",
        reason: "x",
        issue: "https://github.com/orvanohq/orvano/issues/1",
      },
    ];
    assert.match(
      checkCoverage(publicOps, fixtureOps, covered, stale).join("\n"),
      /now has a live scenario/,
    );
    const unexplained = [{ operationId: "Host_Health", reason: "", issue: "soon" }];
    assert.match(
      checkCoverage(publicOps, fixtureOps, [], unexplained).join("\n"),
      /needs a reason and an orvanohq issue link/,
    );
  });

  it("fails a live scenario that calls a fixture operation, and a call that exists nowhere", () => {
    const problems = checkCoverage(
      publicOps,
      fixtureOps,
      [scenario("live", "fixture.getWidget"), scenario("stub", "nothing.here")],
      [],
    );
    assert.match(problems.join("\n"), /calls fixture\.getWidget, which no live operation has/);
    assert.match(problems.join("\n"), /calls nothing\.here, which no stub operation has/);
  });

  it("fails a scenario that breaks the schema, naming the file", () => {
    const { problems } = loadWith(
      "scenarios:\n  - id: Bad\n    title: x\n    mode: live\n    audience: [client]\n    requires: []\n    steps: []\n",
    );
    assert.ok(problems.some((problem) => problem.startsWith("zz-test.yaml/scenarios/0/id")));
    assert.ok(problems.some((problem) => problem.startsWith("zz-test.yaml/scenarios/0/steps")));
  });

  it("fails a stub scenario without its script, and a live one with one", () => {
    const step = "    steps: [{ call: health.get, expect: { result: {} } }]\n";
    const common = "    title: x\n    audience: [client]\n    requires: []\n" + step;
    assert.ok(loadWith(`scenarios:\n  - id: a.b.c\n    mode: stub\n${common}`).problems.length > 0);
    assert.ok(
      loadWith(`scenarios:\n  - id: a.b.c\n    mode: live\n    stub: []\n${common}`).problems
        .length > 0,
    );
  });

  it("fails a duplicate scenario ID", () => {
    const { problems } = loadWith(
      "scenarios:\n  - id: health.get.ok\n    title: x\n    mode: live\n    audience: [client]\n    requires: []\n    steps: [{ call: health.get, expect: { result: {} } }]\n",
    );
    assert.ok(problems.includes("Scenario health.get.ok is defined twice."));
  });
});
