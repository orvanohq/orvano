// Spec 0015 TKN-6: design-tokens.mjs writes design.md's token tables from theme.css, and its check fails when a
// table is stale.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, test } from "node:test";
import { applyTables, buildTables, normalize, parseTheme } from "./design-tokens.mjs";

const root = join(import.meta.dirname, "..", "..");
const css = readFileSync(join(root, "console", "src", "ui", "theme.css"), "utf8");
const design = readFileSync(join(root, "console", "design.md"), "utf8");

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
});
