// Spec 0015 GAL-3: the development gallery never ships. Run after `pnpm build`; fails if the build output has
// gallery.html or a manifest chunk whose source is under src/gallery.
// Usage: node tooling/scripts/check-gallery-not-shipped.mjs [<dist folder>]   (default console/dist)
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The problems found in one build output folder.
 * @param {string} dist
 * @returns {string[]}
 */
export function galleryProblems(dist) {
  const manifestPath = join(dist, ".vite", "manifest.json");
  if (!existsSync(manifestPath)) return [`${manifestPath} is missing; run \`pnpm build\` first.`];
  const problems = [];
  if (existsSync(join(dist, "gallery.html"))) problems.push(`${dist}/gallery.html exists.`);
  /** @type {Record<string, { file: string, src?: string }>} */
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  for (const [key, chunk] of Object.entries(manifest)) {
    const source = chunk.src ?? key;
    if (source === "gallery.html" || source.startsWith("src/gallery/")) {
      problems.push(`${chunk.file} is built from ${source}.`);
    }
  }
  return problems;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const dist = process.argv[2] ?? "console/dist";
  const problems = galleryProblems(dist);
  if (problems.length > 0) {
    console.error(
      `check-gallery-not-shipped: the gallery is in the build:\n  ${problems.join("\n  ")}`,
    );
    process.exit(1);
  }
  console.log("check-gallery-not-shipped: no gallery file or chunk in the build.");
}
