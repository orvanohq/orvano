// Spec 0008 CI-1: changedAreas decides which area workflows ci.yml calls, so a wrong answer either skips a needed
// job or runs everything on every docs PR.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, describe, test } from "node:test";
import { changedAreas } from "./changes.mjs";

const script = join(import.meta.dirname, "changes.mjs");
const none = { server: false, console: false, deploy: false, windows: false };
const all = { server: true, console: true, deploy: true, windows: true };

describe("changedAreas", () => {
  test("docs only changes mark no area", () => {
    assert.deepEqual(changedAreas(["docs/x.md", "README.md", "docs/specs/0008/index.md"]), none);
  });

  test("an empty diff marks no area", () => {
    assert.deepEqual(changedAreas([]), none);
  });

  for (const file of [
    "package.json",
    "pnpm-lock.yaml",
    "pnpm-workspace.yaml",
    ".node-version",
    "vite.config.ts",
    "eslint.config.ts",
    ".oxlintrc.json",
    "tsconfig.json",
    ".editorconfig",
    ".gitattributes",
    "tooling/scripts/changes.mjs",
    "tooling/config/console-restrictions.ts",
    ".github/workflows/ci.yml",
    ".github/workflows/_server.yml",
  ]) {
    test(`global path ${file} marks every area`, () => {
      assert.deepEqual(changedAreas([file]), all);
    });
  }

  test("global paths match only at the root", () => {
    assert.deepEqual(changedAreas(["docs/package.json", "console/vite.config.ts"]), {
      ...none,
      console: true,
      deploy: true,
      windows: true,
    });
  });

  test("other workflows are not global", () => {
    assert.deepEqual(
      changedAreas([".github/workflows/codeql.yml", ".github/workflows/sub/_x.yml"]),
      none,
    );
  });

  test("a server change marks server and deploy", () => {
    assert.deepEqual(changedAreas(["server/src/Orvano.Host/Program.cs"]), {
      ...none,
      server: true,
      deploy: true,
    });
  });

  test("server/.editorconfig also marks windows", () => {
    assert.deepEqual(changedAreas(["server/.editorconfig"]), {
      server: true,
      console: false,
      deploy: true,
      windows: true,
    });
  });

  test("a console change marks console, deploy, and windows", () => {
    assert.deepEqual(changedAreas(["console/src/main.tsx"]), {
      ...none,
      console: true,
      deploy: true,
      windows: true,
    });
  });

  test("a lint fixture marks console only", () => {
    assert.deepEqual(changedAreas(["tooling/lint-fixtures/web-8.tsx"]), {
      ...none,
      console: true,
    });
  });

  test("a contract change marks server and console (spec 0014 CTR-5, CLT-1)", () => {
    assert.deepEqual(changedAreas(["api/openapi.json"]), { ...none, server: true, console: true });
    assert.deepEqual(changedAreas(["api/.spectral.yaml"]), {
      ...none,
      server: true,
      console: true,
    });
  });

  test("deploy files and .dockerignore mark deploy only", () => {
    assert.deepEqual(changedAreas(["deploy/Dockerfile"]), { ...none, deploy: true });
    assert.deepEqual(changedAreas([".dockerignore"]), { ...none, deploy: true });
  });

  for (const file of ["docs/self-hosting.md", "LICENSE", "NOTICE"]) {
    test(`${file}, which a deploy check reads, marks deploy only`, () => {
      assert.deepEqual(changedAreas([file]), { ...none, deploy: true });
    });
  }

  test("only the root LICENSE and NOTICE, and only the self hosting guide, mark deploy", () => {
    assert.deepEqual(
      changedAreas([
        "docs/LICENSE",
        "docs/NOTICE",
        "docs/self-hosting.md.bak",
        "docs/self-hosting/x.md",
      ]),
      none,
    );
  });

  test("the hooks folder marks windows only", () => {
    assert.deepEqual(changedAreas([".vite-hooks/pre-commit"]), { ...none, windows: true });
  });

  test("one global file among docs still marks every area", () => {
    assert.deepEqual(changedAreas(["docs/x.md", "pnpm-lock.yaml"]), all);
  });
});

describe("changes.mjs as a script", () => {
  const dir = mkdtempSync(join(tmpdir(), "orvano-changes-"));
  after(() => rmSync(dir, { recursive: true, force: true }));

  /** @param {string[]} args */
  const run = (args) => {
    const output = join(dir, `output-${String(Math.random()).slice(2)}`);
    writeFileSync(output, "");
    const result = spawnSync("node", [script, ...args], {
      cwd: dir,
      encoding: "utf8",
      env: { ...process.env, GITHUB_OUTPUT: output },
    });
    return { ...result, output: readFileSync(output, "utf8") };
  };

  test("with no base, every area runs", () => {
    const result = run([]);
    assert.equal(result.status, 0);
    assert.match(result.stdout, /no usable base, every area runs/);
    assert.equal(result.output, "server=true\nconsole=true\ndeploy=true\nwindows=true\n");
  });

  test("an all zeros base (a new branch's first push) means every area", () => {
    const result = run(["0000000000000000000000000000000000000000"]);
    assert.equal(result.status, 0);
    assert.equal(result.output, "server=true\nconsole=true\ndeploy=true\nwindows=true\n");
  });

  test("an unknown base means every area", () => {
    const result = run(["deadbeef"]);
    assert.equal(result.status, 0);
    assert.match(result.output, /^server=true$/m);
  });

  test("a real base diffs against HEAD", () => {
    /** @param {string[]} args */
    const git = (...args) => {
      const result = spawnSync("git", args, { cwd: dir, encoding: "utf8" });
      assert.equal(result.status, 0, result.stderr);
      return result.stdout.trim();
    };
    git("init", "-q", "-b", "main");
    git("config", "user.email", "test@example.com");
    git("config", "user.name", "Test");
    writeFileSync(join(dir, "README.md"), "a\n");
    git("add", ".");
    git("commit", "-q", "-m", "base");
    const base = git("rev-parse", "HEAD");
    mkdirSync(join(dir, "docs"));
    writeFileSync(join(dir, "docs", "x.md"), "b\n");
    git("add", ".");
    git("commit", "-q", "-m", "docs");

    const result = run([base]);
    assert.equal(result.status, 0);
    assert.match(result.stdout, /changes: 1 file\(s\) changed/);
    assert.equal(result.output, "server=false\nconsole=false\ndeploy=false\nwindows=false\n");
  });
});
