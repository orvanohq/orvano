// Spec 0008 LNT-3: check:lint-fixtures must fail when a restriction stops firing, or a broken selector would pass
// silently. Each test copies the ESLint config, the restrictions, and the fixtures into a temporary folder (linked to
// the repo's node_modules), changes the copy, and runs the script there.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";

const root = join(import.meta.dirname, "..", "..");
const copied = [
  "package.json",
  "eslint.config.ts",
  ".oxlintrc.json",
  "tooling/config",
  "tooling/lint-fixtures",
  "tooling/scripts/check-lint-fixtures.mjs",
];

/** @param {(dir: string) => void} [change] */
const run = (change) => {
  const dir = mkdtempSync(join(tmpdir(), "orvano-fixtures-"));
  try {
    for (const path of copied) cpSync(join(root, path), join(dir, path), { recursive: true });
    symlinkSync(join(root, "node_modules"), join(dir, "node_modules"), "junction");
    change?.(dir);
    const result = spawnSync("node", ["tooling/scripts/check-lint-fixtures.mjs"], {
      cwd: dir,
      encoding: "utf8",
    });
    return { status: result.status, output: `${result.stdout}${result.stderr}` };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
};

/** @param {string} dir @param {string} name */
const fixture = (dir, name) => join(dir, "tooling", "lint-fixtures", name);

describe("check-lint-fixtures.mjs", { timeout: 120_000 }, () => {
  test("passes when every restriction fires", () => {
    const { status, output } = run();
    assert.equal(status, 0, output);
    assert.match(output, /all 21 restriction rule IDs fire/);
  });

  test("fails when a rule's fixture is removed", () => {
    const { status, output } = run((dir) => {
      rmSync(fixture(dir, "web-8.tsx"));
    });
    assert.equal(status, 1);
    assert.match(output, /WEB-8: no fixture\. Add tooling\/lint-fixtures\/web-8\.tsx/);
  });

  test("fails when a fixture no longer breaks its rule", () => {
    const { status, output } = run((dir) => {
      writeFileSync(
        fixture(dir, "web-8.tsx"),
        "// lint-as: console/src/features/tables/components/rows-page.tsx\nexport const dev = true;\n",
      );
    });
    assert.equal(status, 1);
    assert.match(output, /WEB-8: no fixture fired "WEB-8: read import\.meta\.env only/);
  });

  test("fails when the fixture lives in the rule's allowed home", () => {
    const { status, output } = run((dir) => {
      writeFileSync(
        fixture(dir, "web-8.tsx"),
        "// lint-as: console/src/shared/config.ts\nexport const dev = import.meta.env.DEV;\n",
      );
    });
    assert.equal(status, 1);
    assert.match(output, /WEB-8: no fixture fired/);
  });

  test("fails when a fixture has no lint-as line", () => {
    const { status, output } = run((dir) => {
      writeFileSync(fixture(dir, "web-8.tsx"), "export const dev = import.meta.env.DEV;\n");
    });
    assert.equal(status, 1);
    assert.match(
      output,
      /web-8\.tsx: the first line must be "\/\/ lint-as: <path under console\/src>"/,
    );
  });

  test("fails when a fixture doesn't parse", () => {
    const { status, output } = run((dir) => {
      writeFileSync(
        fixture(dir, "web-4.tsx"),
        "// lint-as: console/src/features/tables/components/rows-page.tsx\nenum {\n",
      );
    });
    assert.equal(status, 1);
    assert.match(output, /web-4\.tsx: Parsing error/);
  });
});
