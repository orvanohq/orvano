// Spec 0015 GAL-3: check-gallery-not-shipped.mjs fails when the build has the gallery's page or a chunk from it.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";
import { galleryProblems } from "./check-gallery-not-shipped.mjs";

/**
 * @param {Record<string, { file: string, src?: string }>} manifest
 * @param {string[]} files
 */
const withDist = (manifest, files = []) => {
  const dist = mkdtempSync(join(tmpdir(), "orvano-gallery-"));
  mkdirSync(join(dist, ".vite"));
  writeFileSync(join(dist, ".vite", "manifest.json"), JSON.stringify(manifest));
  for (const file of files) writeFileSync(join(dist, file), "");
  try {
    return galleryProblems(dist);
  } finally {
    rmSync(dist, { recursive: true, force: true });
  }
};

const app = { "index.html": { file: "assets/index-a1.js", src: "index.html" } };

describe("check-gallery-not-shipped", () => {
  test("0015 AC-20: passes a build with only the app", () => {
    assert.deepEqual(withDist(app, ["index.html"]), []);
  });

  test("0015 AC-20: fails when gallery.html is in the output", () => {
    assert.equal(withDist(app, ["gallery.html"]).length, 1);
  });

  test("0015 AC-20: fails when a chunk is built from src/gallery", () => {
    const problems = withDist({
      ...app,
      "src/gallery/main.tsx": { file: "assets/main-b2.js", src: "src/gallery/main.tsx" },
    });
    assert.deepEqual(problems, ["assets/main-b2.js is built from src/gallery/main.tsx."]);
  });

  test("fails when there is no build to check", () => {
    const dist = mkdtempSync(join(tmpdir(), "orvano-gallery-"));
    try {
      assert.match(galleryProblems(dist)[0] ?? "", /run `pnpm build` first/);
    } finally {
      rmSync(dist, { recursive: true, force: true });
    }
  });

  describe("command line", () => {
    const script = join(import.meta.dirname, "check-gallery-not-shipped.mjs");
    /** @param {Record<string, { file: string, src?: string }>} manifest @param {string[]} files */
    const run = (manifest, files = []) => {
      const dist = mkdtempSync(join(tmpdir(), "orvano-gallery-"));
      mkdirSync(join(dist, ".vite"));
      writeFileSync(join(dist, ".vite", "manifest.json"), JSON.stringify(manifest));
      for (const file of files) writeFileSync(join(dist, file), "");
      try {
        return spawnSync("node", [script, dist], { encoding: "utf8" });
      } finally {
        rmSync(dist, { recursive: true, force: true });
      }
    };

    test("0015 AC-20: exits 0 and says so for a clean build", () => {
      const result = run(app, ["index.html"]);
      assert.equal(result.status, 0);
      assert.match(result.stdout, /no gallery file or chunk in the build/);
    });

    test("0015 AC-20: exits 1 and lists every problem, one per line", () => {
      const result = run(
        {
          ...app,
          "src/gallery/main.tsx": { file: "assets/main-b2.js", src: "src/gallery/main.tsx" },
        },
        ["gallery.html"],
      );
      assert.equal(result.status, 1);
      assert.match(
        result.stderr,
        /the gallery is in the build:\n {2}.*gallery\.html exists\.\n {2}assets\/main-b2\.js/,
      );
    });

    test("exits 1 when the folder has no build", () => {
      const dist = mkdtempSync(join(tmpdir(), "orvano-gallery-"));
      try {
        const result = spawnSync("node", [script, dist], { encoding: "utf8" });
        assert.equal(result.status, 1);
        assert.match(result.stderr, /run `pnpm build` first/);
      } finally {
        rmSync(dist, { recursive: true, force: true });
      }
    });
  });
});
