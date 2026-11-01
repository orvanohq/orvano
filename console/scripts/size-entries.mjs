// Spec 0003 BDL-1, spec 0015 STY-4, spec 0008 CI-2: turns the Vite manifest from the last build into size-limit
// entries, so the budgets follow hashed file names. Run after `vite build`; size-limit reads the .size-limit.json it writes.
import { existsSync, readFileSync, writeFileSync } from "node:fs";

const firstLoadLimit = "230 KB"; // spec 0015 STY-8
const perFileLimit = "80 KB";
const cssLimit = "30 KB";

// The first load (BDL-1): the root, shell, org, project, and project overview route files, plus the `en` catalog
// main.tsx loads before the first render (spec 0017 CNS-7).
const firstLoadRoutes = [
  /^src\/locales\/en\/messages\.po$/,
  /^src\/routes\/__root\.tsx/,
  /^src\/routes\/_shell(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId\/\$projectId(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId\/\$projectId\/index\.tsx/,
];

/** @typedef {Record<string, { file: string, isEntry?: boolean, imports?: string[] }>} Manifest */

/** @returns {Manifest} */
function readManifest() {
  const path = "dist/.vite/manifest.json";
  if (!existsSync(path)) {
    console.error(`size-entries: ${path} is missing. Run the build that writes it first.`);
    process.exit(1);
  }
  return JSON.parse(readFileSync(path, "utf8"));
}

/** Each chunk's file plus everything it imports statically. */
function withStaticImports(manifest, key, files = new Set()) {
  const chunk = manifest[key];
  if (!chunk || files.has(chunk.file)) return files;
  files.add(chunk.file);
  for (const imported of chunk.imports ?? []) withStaticImports(manifest, imported, files);
  return files;
}

/** The entry chunk and the chunks of the matching keys, plus their static imports. */
function firstLoadOf(manifest, routes) {
  const files = new Set();
  for (const [key, chunk] of Object.entries(manifest)) {
    if (chunk.isEntry || routes.some((route) => route.test(key)))
      withStaticImports(manifest, key, files);
  }
  return files;
}

/** Every JavaScript file, sorted, once. */
const javascriptOf = (manifest) =>
  [...new Set(Object.values(manifest).map((chunk) => chunk.file))]
    .filter((file) => file.endsWith(".js"))
    .toSorted();

/** One 80 KB entry per JavaScript file (BDL-1). */
const perFileEntries = (manifest) =>
  javascriptOf(manifest).map((file) => ({
    name: `${file} (BDL-1, one file)`,
    path: `dist/${file}`,
    limit: perFileLimit,
    gzip: true,
  }));

const manifest = readManifest();
const firstLoad = firstLoadOf(manifest, firstLoadRoutes);

const entries = [
  {
    name: "First load (BDL-1)",
    path: [...firstLoad].filter((file) => file.endsWith(".js")).map((file) => `dist/${file}`),
    limit: firstLoadLimit,
    gzip: true,
  },
  ...perFileEntries(manifest),
  // STY-4: the one stylesheet (build.cssCodeSplit is off).
  {
    name: "CSS (STY-4)",
    path: [...new Set(Object.values(manifest).map((chunk) => chunk.file))]
      .filter((file) => file.endsWith(".css"))
      .map((file) => `dist/${file}`),
    limit: cssLimit,
    gzip: true,
  },
];

writeFileSync(".size-limit.json", `${JSON.stringify(entries, null, 2)}\n`);

console.log(`size-entries: ${entries.length} entries (${firstLoad.size} first load files)`);
