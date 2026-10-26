// Spec 0015 TKN-6: console/src/ui/theme.css is the only source of token values. This writes the token tables into
// console/design.md between `<!-- tokens:<name>:start -->` and `<!-- tokens:<name>:end -->` markers.
// Usage: node tooling/scripts/design-tokens.mjs --write | --check
//   --write (in `pnpm fix`, before Oxfmt formats the file) updates design.md.
//   --check (in `pnpm check`) generates in memory and fails when a block is out of date. Tables are compared with
//   their padding collapsed, so the result doesn't depend on how Oxfmt aligns columns (`vp check` checks that).
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

/**
 * @typedef {{ name: string, value: string, use: string, context: string }} Declaration
 * @typedef {{ header: string[], rows: string[][] }} Table
 */

/**
 * Reads every custom property declaration with the block it sits in (`@theme`, `[data-theme="dark"]`, or nested
 * like `@media (…) > :root:not([data-theme])`) and the `use:` comment on the line above it.
 * @param {string} css
 * @returns {Declaration[]}
 */
export function parseTheme(css) {
  /** @type {Declaration[]} */
  const declarations = [];
  /** @type {string[]} */
  const stack = [];
  let use = "";
  let buffer = "";
  for (let index = 0; index < css.length; index++) {
    if (css.startsWith("/*", index)) {
      const end = css.indexOf("*/", index + 2);
      const comment = css.slice(index + 2, end === -1 ? css.length : end).trim();
      if (comment.startsWith("use:")) use = comment.slice(4).replaceAll(/\s+/g, " ").trim();
      index = end === -1 ? css.length : end + 1;
      continue;
    }
    const char = css[index];
    if (char === "{") {
      stack.push(buffer.replaceAll(/\s+/g, " ").trim());
      buffer = "";
    } else if (char === "}") {
      stack.pop();
      buffer = "";
    } else if (char === ";") {
      const match = /^\s*(--[\w-]+)\s*:\s*([\s\S]*)$/.exec(buffer);
      if (match?.[1] && match[2] !== undefined) {
        const value = match[2]
          .replaceAll(/\s+/g, " ")
          .replaceAll("( ", "(")
          .replaceAll(" )", ")")
          .trim();
        declarations.push({ name: match[1], value, use, context: stack.join(" > ") });
      }
      buffer = "";
      use = "";
    } else {
      buffer += char;
    }
  }
  return declarations;
}

/**
 * @param {Declaration[]} declarations
 * @param {string} context
 * @param {RegExp} name
 */
const pick = (declarations, context, name) =>
  declarations.filter(
    (declaration) => declaration.context === context && name.test(declaration.name),
  );

/** @param {string} value */
const code = (value) => `\`${value}\``;

/** @param {Declaration[]} entries */
const valueAndUse = (entries) =>
  entries.map((entry) => [code(entry.name), code(entry.value), entry.use]);

/**
 * Builds every generated table from theme.css's declarations.
 * @param {Declaration[]} declarations
 * @returns {Map<string, Table>}
 */
export function buildTables(declarations) {
  const theme = "@theme";
  const dark = new Map(
    pick(declarations, '[data-theme="dark"]', /^--color-/).map((entry) => [
      entry.name,
      entry.value,
    ]),
  );

  const compact = new Map(
    pick(declarations, '[data-density="compact"]', /^--row-height$/).map((entry) => [
      entry.name,
      entry.value,
    ]),
  );
  // The :root block that holds the layers and durations (the density one holds only --row-height).
  const plain = pick(declarations, ":root", /^--(z|duration)-/);

  return new Map([
    [
      "colors",
      {
        header: ["Token", "Light", "Dark", "Use"],
        rows: pick(declarations, theme, /^--color-/).map((entry) => [
          code(entry.name),
          code(entry.value),
          code(dark.get(entry.name) ?? entry.value),
          entry.use,
        ]),
      },
    ],
    [
      "fonts",
      {
        header: ["Token", "Value"],
        rows: pick(declarations, theme, /^--font-/).map((entry) => [
          code(entry.name),
          code(entry.value),
        ]),
      },
    ],
    [
      "type",
      {
        header: ["Token", "Size", "Line height (`--text-<name>--line-height`)", "Use"],
        rows: pick(declarations, theme, /^--text-[\w]+$/).map((entry) => [
          code(entry.name),
          code(entry.value),
          code(
            declarations.find((other) => other.name === `${entry.name}--line-height`)?.value ?? "",
          ),
          entry.use,
        ]),
      },
    ],
    [
      "layout",
      {
        header: ["Token", "Value", "Use"],
        rows: valueAndUse(pick(declarations, theme, /^--spacing-/)),
      },
    ],
    [
      "density",
      {
        header: ["Variable", "Comfortable", "Compact", "Use"],
        rows: pick(declarations, ":root", /^--row-height$/).map((entry) => [
          code(entry.name),
          code(entry.value),
          code(compact.get(entry.name) ?? entry.value),
          entry.use,
        ]),
      },
    ],
    [
      "shape",
      {
        header: ["Token", "Value", "Use"],
        rows: valueAndUse(pick(declarations, theme, /^--(radius|shadow)-/)),
      },
    ],
    [
      "layers",
      {
        header: ["Variable", "Value", "Use"],
        rows: valueAndUse(plain.filter((entry) => entry.name.startsWith("--z-"))),
      },
    ],
    [
      "motion",
      {
        header: ["Token", "Value", "Use"],
        rows: valueAndUse([
          ...plain.filter((entry) => entry.name.startsWith("--duration-")),
          ...pick(declarations, theme, /^--ease-/),
        ]),
      },
    ],
  ]);
}

/** @param {string} cell */
const escape = (cell) => cell.replaceAll("|", "\\|");

/**
 * A Markdown table padded the way Oxfmt pads one.
 * @param {Table} table
 */
export function renderTable({ header, rows }) {
  const all = [header, ...rows].map((row) => row.map(escape));
  const widths = header.map((_, column) =>
    Math.max(3, ...all.map((row) => (row[column] ?? "").length)),
  );
  const line = (/** @type {string[]} */ row) =>
    `| ${row.map((cell, column) => cell.padEnd(widths[column] ?? 0)).join(" | ")} |`;
  const [head = [], ...body] = all;
  return [
    line(head),
    `| ${widths.map((width) => "-".repeat(width)).join(" | ")} |`,
    ...body.map(line),
  ].join("\n");
}

/**
 * Replaces each generated block in the Markdown. Throws when a table has no markers.
 * @param {string} markdown
 * @param {Map<string, Table>} tables
 */
export function applyTables(markdown, tables) {
  let result = markdown;
  for (const [name, table] of tables) {
    const start = `<!-- tokens:${name}:start -->`;
    const end = `<!-- tokens:${name}:end -->`;
    const from = result.indexOf(start);
    const to = result.indexOf(end);
    if (from === -1 || to < from) throw new Error(`design.md has no ${start} … ${end} block.`);
    result = `${result.slice(0, from + start.length)}\n\n${renderTable(table)}\n\n${result.slice(to)}`;
  }
  return result;
}

/** Collapses table padding, so a compare ignores column alignment. @param {string} markdown */
export const normalize = (markdown) =>
  markdown
    .split("\n")
    .map((line) =>
      line.startsWith("|")
        ? line.replaceAll(/ {2,}/g, " ").replaceAll(/-{3,}/g, "---").replaceAll(" |", "|")
        : line,
    )
    .join("\n");

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const mode = process.argv[2];
  if (mode !== "--write" && mode !== "--check") {
    console.error("Usage: node tooling/scripts/design-tokens.mjs --write | --check");
    process.exit(2);
  }
  const root = fileURLToPath(new URL("../../", import.meta.url));
  const designPath = `${root}console/design.md`;
  const tables = buildTables(parseTheme(readFileSync(`${root}console/src/ui/theme.css`, "utf8")));
  const current = readFileSync(designPath, "utf8");
  const generated = applyTables(current, tables);
  if (mode === "--write") {
    if (generated !== current) writeFileSync(designPath, generated);
    console.log("design-tokens: design.md's token tables are written from theme.css.");
  } else if (normalize(generated) !== normalize(current)) {
    console.error(
      "design-tokens: console/design.md's token tables don't match console/src/ui/theme.css. Run `pnpm fix`.",
    );
    process.exit(1);
  } else {
    console.log("design-tokens: design.md's token tables match theme.css.");
  }
}
