// Fails when a raw colour appears in console/src outside the generated src/styles/tokens.css (spec 0003, AC-5).
// Every colour must come from a token, through the shadcn variables or the token utilities (bg-surface, text-ink-muted).
import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "../src");
const exempt = path.join(root, "styles", "tokens.css");
const extensions = new Set([".ts", ".tsx", ".css"]);

const palette = [
  "slate",
  "gray",
  "zinc",
  "neutral",
  "stone",
  "red",
  "orange",
  "amber",
  "yellow",
  "lime",
  "green",
  "emerald",
  "teal",
  "cyan",
  "sky",
  "blue",
  "indigo",
  "violet",
  "purple",
  "fuchsia",
  "pink",
  "rose",
];
const utility =
  "(?:bg|text|border(?:-[xytrblse])?|ring(?:-offset)?|outline|fill|stroke|from|via|to|divide|placeholder|caret|accent|decoration|shadow|inset-shadow|inset-ring)";

const rules = [
  {
    name: "hex colour",
    pattern: /#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b/g,
  },
  {
    name: "Tailwind arbitrary colour",
    pattern: new RegExp(
      `\\b${utility}-\\[(?:#|rgba?\\(|hsla?\\(|oklch\\(|oklab\\(|lab\\(|lch\\(|color-mix\\()`,
      "g",
    ),
  },
  {
    name: "Tailwind palette class",
    pattern: new RegExp(
      `\\b${utility}-(?:${palette.join("|")})-\\d{2,3}\\b`,
      "g",
    ),
  },
  {
    name: "white or black utility",
    pattern: new RegExp(`\\b${utility}-(?:white|black)\\b`, "g"),
  },
];

// A colour function counts only when it holds a literal: rgb(var(--x)) is fine, rgb(0 0 0) is not.
const colourFunction =
  /\b(rgba?|hsla?|oklch|oklab|lab|lch|color-mix)\(([^()]*(?:\([^()]*\)[^()]*)*)\)/g;
const literalInside = (fn, args) => {
  let rest = args.replace(/var\([^()]*\)/g, "").replace(/[\s_,/]+/g, " ");
  if (fn === "color-mix") {
    rest = rest.replace(/\bin\s+[a-z-]+/g, "").replace(/\b\d+(?:\.\d+)?%/g, "");
  }
  return rest.trim().length > 0;
};

async function* walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      yield* walk(full);
    } else if (extensions.has(path.extname(entry.name)) && full !== exempt) {
      yield full;
    }
  }
}

const problems = [];
for await (const file of walk(root)) {
  const lines = (await readFile(file, "utf8")).split("\n");
  lines.forEach((line, index) => {
    const where = `${path.relative(process.cwd(), file)}:${index + 1}`;
    for (const rule of rules) {
      for (const match of line.matchAll(rule.pattern)) {
        problems.push(`${where}  ${rule.name}: ${match[0]}`);
      }
    }
    for (const match of line.matchAll(colourFunction)) {
      if (literalInside(match[1], match[2])) {
        problems.push(`${where}  colour function with a literal: ${match[0]}`);
      }
    }
  });
}

if (problems.length > 0) {
  console.error(
    "Raw colours found. Use a token instead (see docs/design/design-system/README.md):",
  );
  for (const problem of problems) {
    console.error(`  ${problem}`);
  }
  process.exit(1);
}
console.log("No raw colours in src outside tokens.css.");
