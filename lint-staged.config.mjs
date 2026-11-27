// Runs before each commit on the staged files only (AGENTS.md, Tooling).
// The full build, typecheck and tests run in CI.
import path from "node:path";

const quote = (files) => files.map((f) => JSON.stringify(f)).join(" ");

// dotnet format silently skips absolute paths in --include, so pass repo relative ones.
const relative = (files) => files.map((f) => path.relative(process.cwd(), f));

const eslintIn = (pkg) => (files) =>
  `pnpm --filter ${pkg} exec eslint --fix --max-warnings 0 ${quote(files)}`;

export default {
  "{console,website}/**/*.{js,mjs,cjs,ts,tsx,jsx,astro,json,css,md,mdx,yml,yaml}":
    "prettier --write --ignore-unknown",
  "console/**/*.{js,mjs,ts,tsx,jsx}": eslintIn("@orvano/console"),
  "website/**/*.{js,mjs,ts,astro}": eslintIn("@orvano/website"),
  "**/*.cs": (files) => `dotnet format Orvano.slnx --include ${quote(relative(files))}`,
};
