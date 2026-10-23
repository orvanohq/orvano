// Spec 0014 AC-2, CLT-1, CLT-2: every Orvano rule in api/.spectral.yaml fires on the violations fixture, naming the
// rule, and the committed contract documents pass with no warning.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, test } from "node:test";

const root = join(import.meta.dirname, "..", "..");
const spectral = join(root, "node_modules", "@stoplight", "spectral-cli", "dist", "index.js");
const ruleset = join(root, "api", ".spectral.yaml");

/** @param {string[]} documents */
const lint = (documents) =>
  spawnSync(
    process.execPath,
    [
      spectral,
      "lint",
      ...documents,
      "--ruleset",
      ruleset,
      "--fail-severity",
      "warn",
      "-f",
      "json",
      "--quiet",
    ],
    { cwd: root, encoding: "utf8" },
  );

const orvanoRules = [...readFileSync(ruleset, "utf8").matchAll(/^ {2}(orvano-[a-z0-9-]+):$/gm)].map(
  (match) => match[1] ?? "",
);

describe("api/.spectral.yaml", () => {
  test("declares the Orvano rules", () => {
    assert.ok(orvanoRules.length >= 15, `found only ${orvanoRules.length} rules`);
  });

  test("fires every Orvano rule on the violations fixture", () => {
    const result = lint(["api/spectral/test/violations.openapi.json"]);
    assert.notEqual(result.status, 0);
    /** @type {{ code: string }[]} */
    const findings = JSON.parse(result.stdout);
    const fired = new Set(findings.map((finding) => finding.code));
    assert.deepEqual(
      orvanoRules.filter((rule) => !fired.has(rule)),
      [],
      "these rules did not fire on the fixture",
    );
  });

  test("passes the committed contract documents", () => {
    const result = lint(["api/openapi.json", "api/console.openapi.json"]);
    assert.equal(result.status, 0, result.stdout + result.stderr);
  });
});
