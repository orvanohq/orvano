// Spec 0003 BDL-1, spec 0008 CI-2: console/scripts/size-entries.mjs turns a Vite manifest into size-limit budgets.
// Each test writes a manifest to a temporary folder, runs the script there, and reads the .size-limit.json back.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";

const script = join(import.meta.dirname, "..", "..", "console", "scripts", "size-entries.mjs");

/**
 * @typedef {{ name: string, path: string | string[], limit: string, gzip: boolean }} Entry
 * @param {Record<string, { file: string, isEntry?: boolean, imports?: string[], dynamicImports?: string[] }>} manifest
 * @returns {{ status: number | null, stdout: string, entries: Entry[] }}
 */
const run = (manifest) => {
  const dir = mkdtempSync(join(tmpdir(), "orvano-size-"));
  try {
    mkdirSync(join(dir, "dist", ".vite"), { recursive: true });
    writeFileSync(join(dir, "dist", ".vite", "manifest.json"), JSON.stringify(manifest));
    const result = spawnSync("node", [script], { cwd: dir, encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    const entries = /** @type {Entry[]} */ (
      JSON.parse(readFileSync(join(dir, ".size-limit.json"), "utf8"))
    );
    return { status: result.status, stdout: result.stdout, entries };
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
};

const manifest = {
  "index.html": { file: "assets/index-a1.js", isEntry: true, imports: ["_vendor-b2.js"] },
  "_vendor-b2.js": { file: "assets/vendor-b2.js" },
  "_shared-c3.js": { file: "assets/shared-c3.js", imports: ["_vendor-b2.js"] },
  "src/routes/__root.tsx": { file: "assets/root-d4.js", imports: ["_shared-c3.js"] },
  "src/routes/_shell.tsx": { file: "assets/shell-e5.js" },
  "src/routes/_shell/$orgId/$projectId/index.tsx": { file: "assets/overview-f6.js" },
  "src/routes/_shell/$orgId/$projectId/settings.tsx": {
    file: "assets/settings-g7.js",
    imports: ["_lazy-h8.js"],
  },
  "_lazy-h8.js": { file: "assets/lazy-h8.js" },
  "src/locales/en/messages.po": { file: "assets/messages-en.js" },
  "src/locales/de/messages.po": { file: "assets/messages-de.js" },
  "src/routes/_shell/$orgId.tsx": {
    file: "assets/org-i9.js",
    dynamicImports: ["src/routes/_shell/$orgId/$projectId/settings.tsx"],
  },
  "style.css": { file: "assets/style-j0.css", isEntry: true },
};

describe("size-entries.mjs", () => {
  test("the first load holds the entry, first load routes, the en catalog, and their static imports", () => {
    const [firstLoad] = run(manifest).entries;
    assert.equal(firstLoad?.name, "First load (BDL-1)");
    assert.equal(firstLoad.limit, "200 KB");
    assert.equal(firstLoad.gzip, true);
    assert.deepEqual([.../** @type {string[]} */ (firstLoad.path)].toSorted(), [
      "dist/assets/index-a1.js",
      "dist/assets/messages-en.js",
      "dist/assets/org-i9.js",
      "dist/assets/overview-f6.js",
      "dist/assets/root-d4.js",
      "dist/assets/shared-c3.js",
      "dist/assets/shell-e5.js",
      "dist/assets/vendor-b2.js",
    ]);
  });

  test("the first load leaves out other routes, other catalogs, dynamic imports, and CSS", () => {
    const [firstLoad] = run(manifest).entries;
    const paths = /** @type {string[]} */ (firstLoad?.path);
    for (const file of ["settings-g7.js", "lazy-h8.js", "messages-de.js", "style-j0.css"]) {
      assert.ok(!paths.includes(`dist/assets/${file}`), file);
    }
  });

  test("every JS file gets its own 80 KB entry, sorted, once", () => {
    const manifestWithDuplicate = {
      ...manifest,
      "src/other.ts": { file: "assets/vendor-b2.js" },
    };
    const perFile = run(manifestWithDuplicate).entries.slice(1, -1);
    assert.deepEqual(
      perFile.map((entry) => entry.path),
      [
        "dist/assets/index-a1.js",
        "dist/assets/lazy-h8.js",
        "dist/assets/messages-de.js",
        "dist/assets/messages-en.js",
        "dist/assets/org-i9.js",
        "dist/assets/overview-f6.js",
        "dist/assets/root-d4.js",
        "dist/assets/settings-g7.js",
        "dist/assets/shared-c3.js",
        "dist/assets/shell-e5.js",
        "dist/assets/vendor-b2.js",
      ],
    );
    for (const entry of perFile) {
      assert.equal(entry.limit, "80 KB");
      assert.equal(entry.gzip, true);
      assert.equal(entry.name, `${String(entry.path).replace(/^dist\//, "")} (BDL-1, one file)`);
    }
  });

  test("0015 AC-3: the stylesheet gets one 30 KB entry, last", () => {
    const css = run(manifest).entries.at(-1);
    assert.deepEqual(css, {
      name: "CSS (STY-4)",
      path: ["dist/assets/style-j0.css"],
      limit: "30 KB",
      gzip: true,
    });
  });

  test("import cycles don't loop", () => {
    const { entries } = run({
      "index.html": { file: "assets/a.js", isEntry: true, imports: ["_b.js"] },
      "_b.js": { file: "assets/b.js", imports: ["index.html"] },
    });
    assert.deepEqual(entries[0]?.path, ["dist/assets/a.js", "dist/assets/b.js"]);
  });

  test("reports how many entries and first load files it wrote", () => {
    assert.match(run(manifest).stdout, /size-entries: 13 entries \(9 first load files\)/);
  });
});
