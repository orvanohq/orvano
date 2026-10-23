// Spec 0008 CI-1: which areas a push or pull request changed, so ci.yml calls only those area workflows.
// Usage: node tooling/scripts/changes.mjs [<base sha>]
// Writes `<area>=true|false` lines to $GITHUB_OUTPUT (or stdout locally). With no usable base (workflow_dispatch,
// workflow_call, a new branch's first push), every area counts as changed.
import { spawnSync } from "node:child_process";
import { appendFileSync } from "node:fs";
import { resolve } from "node:path";

/** Root files and folders that change every area (CI-1's global list). */
const globalPaths = [
  /^package\.json$/,
  /^pnpm-lock\.yaml$/,
  /^pnpm-workspace\.yaml$/,
  /^\.node-version$/,
  /^vite\.config\.ts$/,
  /^eslint\.config\.ts$/,
  /^\.oxlintrc\.json$/,
  /^tsconfig\.json$/,
  /^\.editorconfig$/,
  /^\.gitattributes$/,
  /^tooling\/scripts\//,
  /^tooling\/config\//,
  /^\.github\/workflows\/ci\.yml$/,
  /^\.github\/workflows\/_[^/]+\.yml$/,
];

/** Area → paths that change it. `repo` always runs, so it isn't listed. */
const areaPaths = {
  // api/: the server job checks the committed contract for drift, the console job lints it and its API types.
  server: [/^server\//, /^api\//],
  console: [/^console\//, /^tooling\/lint-fixtures\//, /^api\//],
  deploy: [/^deploy\//, /^\.dockerignore$/, /^server\//, /^console\//],
  windows: [
    /^console\//,
    /^\.vite-hooks\//,
    /^tooling\/scripts\/format-cs\.mjs$/,
    /^server\/\.editorconfig$/,
  ],
};

/** @param {string[]} files */
export function changedAreas(files) {
  const everything = files.some((file) => globalPaths.some((path) => path.test(file)));
  return Object.fromEntries(
    Object.entries(areaPaths).map(([area, paths]) => [
      area,
      everything || files.some((file) => paths.some((path) => path.test(file))),
    ]),
  );
}

/** @param {string | undefined} base */
function changedFiles(base) {
  if (!base || /^0+$/.test(base)) return undefined;
  const result = spawnSync("git", ["diff", "--name-only", `${base}...HEAD`], { encoding: "utf8" });
  if (result.status !== 0) return undefined;
  return result.stdout.split("\n").filter(Boolean);
}

// Run as a script (CI), or import changedAreas to check sample diffs.
if (process.argv[1] && resolve(process.argv[1]) === import.meta.filename) {
  const files = changedFiles(process.argv[2]);
  const areas = files
    ? changedAreas(files)
    : Object.fromEntries(Object.keys(areaPaths).map((area) => [area, true]));
  const lines = Object.entries(areas).map(([area, changed]) => `${area}=${String(changed)}`);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `${lines.join("\n")}\n`);
  console.log(
    files ? `changes: ${files.length} file(s) changed` : "changes: no usable base, every area runs",
  );
  console.log(lines.join("\n"));
}
