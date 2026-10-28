// Spec 0015 TKN-6: design-tokens.mjs writes design.md's token tables from theme.css, and its check fails when a
// table is stale.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";
import { applyTables, buildTables, normalize, parseTheme } from "./design-tokens.mjs";

const root = join(import.meta.dirname, "..", "..");
const css = readFileSync(join(root, "console", "src", "ui", "theme.css"), "utf8");
const design = readFileSync(join(root, "console", "design.md"), "utf8");

// The script finds design.md and theme.css from its own path, so each test runs a copy in a scratch repo.
/** @param {string} designText @param {string[]} args */
const run = (designText, args) => {
  // The real path: on macOS the temp folder is a symlink, and the script compares its own resolved path.
  const dir = realpathSync(mkdtempSync(join(tmpdir(), "orvano-tokens-")));
  try {
    mkdirSync(join(dir, "tooling", "scripts"), { recursive: true });
    mkdirSync(join(dir, "console", "src", "ui"), { recursive: true });
    copyFileSync(
      join(import.meta.dirname, "design-tokens.mjs"),
      join(dir, "tooling", "scripts", "design-tokens.mjs"),
    );
    writeFileSync(join(dir, "console", "src", "ui", "theme.css"), css);
    writeFileSync(join(dir, "console", "design.md"), designText);
    const result = spawnSync(
      "node",
      [join(dir, "tooling", "scripts", "design-tokens.mjs"), ...args],
      {
        encoding: "utf8",
      },
    );
    return { ...result, written: readFileSync(join(dir, "console", "design.md"), "utf8") };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
};
const stale = design.replace(/`--color-fg` +\| `[^`]+`/, "`--color-fg` | `oklch(0.9 0 0)`");

describe("design-tokens", () => {
  test("0015 AC-1: the committed design.md matches theme.css", () => {
    const generated = applyTables(design, buildTables(parseTheme(css)));
    assert.equal(normalize(generated), normalize(design));
  });

  test("0015 AC-1: changing a token without regenerating makes the check fail", () => {
    const changed = css.replace(
      "--color-fg: oklch(0.21 0.006 286);",
      "--color-fg: oklch(0.3 0 0);",
    );
    assert.notEqual(changed, css);
    const generated = applyTables(design, buildTables(parseTheme(changed)));
    assert.notEqual(normalize(generated), normalize(design));
    assert.match(generated, /`--color-fg` +\| `oklch\(0\.3 0 0\)`/);
  });

  test("reads the use comment on the line above and the block each value sits in", () => {
    const declarations = parseTheme(
      `@theme {\n  /* use: Body text */\n  --color-fg: oklch(\n    0.2 0 0\n  );\n  --color-bg: white;\n}\n` +
        `[data-theme="dark"] { --color-fg: black; }\n`,
    );
    assert.deepEqual(declarations, [
      { name: "--color-fg", value: "oklch(0.2 0 0)", use: "Body text", context: "@theme" },
      { name: "--color-bg", value: "white", use: "", context: "@theme" },
      { name: "--color-fg", value: "black", use: "", context: '[data-theme="dark"]' },
    ]);
  });

  test("fills a table's dark column from the dark block", () => {
    const tables = buildTables(
      parseTheme(
        `@theme { /* use: Body text */ --color-fg: white; }\n[data-theme="dark"] { --color-fg: black; }`,
      ),
    );
    assert.deepEqual(tables.get("colors")?.rows, [
      ["`--color-fg`", "`white`", "`black`", "Body text"],
    ]);
  });

  test("throws when design.md lacks a table's markers", () => {
    assert.throws(() => applyTables("# No markers", buildTables(parseTheme(css))), /tokens:colors/);
  });

  describe("command line", () => {
    test("0015 AC-1: --check passes on an up to date design.md", () => {
      const result = run(design, ["--check"]);
      assert.equal(result.status, 0);
      assert.match(result.stdout, /match theme\.css/);
    });

    test("0015 AC-1: --check exits 1, names pnpm fix, and leaves a stale design.md alone", () => {
      assert.notEqual(stale, design);
      const result = run(stale, ["--check"]);
      assert.equal(result.status, 1);
      assert.match(result.stderr, /don't match .*Run `pnpm fix`/);
      assert.equal(result.written, stale);
    });

    test("0015 AC-1: --write regenerates a stale design.md so it matches", () => {
      const result = run(stale, ["--write"]);
      assert.equal(result.status, 0);
      assert.equal(normalize(result.written), normalize(design));
    });

    test("exits 2 with the usage line for a missing or unknown mode", () => {
      for (const args of [[], ["--fix"]]) {
        const result = run(design, args);
        assert.equal(result.status, 2);
        assert.match(
          result.stderr,
          /Usage: node tooling\/scripts\/design-tokens\.mjs --write \| --check/,
        );
      }
    });
  });
});
