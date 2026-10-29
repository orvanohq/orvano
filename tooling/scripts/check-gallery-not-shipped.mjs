// Spec 0015 GAL-3: the development gallery never ships. Run after `pnpm build`; fails if the build output has
// gallery.html, a manifest chunk whose source is under src/gallery, or a script holding a string only the gallery's
// fixtures contain (gallery code inlined into an app chunk has no manifest entry of its own).
// Usage: node tooling/scripts/check-gallery-not-shipped.mjs [<dist folder>]   (default console/dist)
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

/** Strings only console/src/gallery/fixtures.ts contains (the test checks they are still there). */
export const galleryMarkers = ["usr_ada"];

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
  const assets = join(dist, "assets");
  const scripts = existsSync(assets)
    ? readdirSync(assets).filter((file) => file.endsWith(".js"))
    : [];
  for (const file of scripts) {
    const text = readFileSync(join(assets, file), "utf8");
    for (const marker of galleryMarkers.filter((value) => text.includes(value))) {
      problems.push(`assets/${file} holds the gallery fixture string "${marker}".`);
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
