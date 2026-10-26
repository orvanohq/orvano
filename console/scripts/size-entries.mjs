// Spec 0003 BDL-1, spec 0015 STY-4, spec 0008 CI-2: turns the Vite manifest from the last build into size-limit
// entries, so the budgets follow hashed file names. Run after `vite build`; size-limit reads the .size-limit.json it writes.
import { readFileSync, writeFileSync } from "node:fs";

const firstLoadLimit = "200 KB";
const perFileLimit = "80 KB";
const cssLimit = "30 KB";

// The first load (BDL-1): the root, shell, org, project, and project overview route files, plus the `en` catalog
// main.tsx loads before the first render. Routes that don't exist yet match nothing.
const firstLoadRoutes = [
  /^src\/locales\/en\/messages\.po$/,
  /^src\/routes\/__root\.tsx/,
  /^src\/routes\/_shell(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId\/\$projectId(\.tsx|\/route\.tsx)/,
  /^src\/routes\/_shell\/\$orgId\/\$projectId\/index\.tsx/,
];

/** @type {Record<string, { file: string, isEntry?: boolean, imports?: string[] }>} */
const manifest = JSON.parse(readFileSync("dist/.vite/manifest.json", "utf8"));

/** Each chunk's file plus everything it imports statically. */
function withStaticImports(key, files = new Set()) {
  const chunk = manifest[key];
  if (!chunk || files.has(chunk.file)) return files;
  files.add(chunk.file);
  for (const imported of chunk.imports ?? []) withStaticImports(imported, files);
  return files;
}

const firstLoad = new Set();
for (const [key, chunk] of Object.entries(manifest)) {
  if (chunk.isEntry || firstLoadRoutes.some((route) => route.test(key)))
    withStaticImports(key, firstLoad);
}

const javascript = [...new Set(Object.values(manifest).map((chunk) => chunk.file))]
  .filter((file) => file.endsWith(".js"))
  .toSorted();

const entries = [
  {
    name: "First load (BDL-1)",
    path: [...firstLoad].filter((file) => file.endsWith(".js")).map((file) => `dist/${file}`),
    limit: firstLoadLimit,
    gzip: true,
  },
  ...javascript.map((file) => ({
    name: `${file} (BDL-1, one file)`,
    path: `dist/${file}`,
    limit: perFileLimit,
    gzip: true,
  })),
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
