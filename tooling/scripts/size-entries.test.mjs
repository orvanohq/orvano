// Spec 0003 BDL-1, spec 0008 CI-2: console/scripts/size-entries.mjs turns a Vite manifest into size-limit budgets.
// Each test writes the app and shell probe manifests (spec 0015 STY-9) to a temporary folder, runs the script there,
// and reads the .size-limit.json back.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";

const script = join(import.meta.dirname, "..", "..", "console", "scripts", "size-entries.mjs");

/**
 * @typedef {{ name: string, path: string | string[], limit: string, gzip: boolean }} Entry
 * @typedef {Record<string, { file: string, isEntry?: boolean, imports?: string[], dynamicImports?: string[] }>} Manifest
 */

/**
 * Writes each manifest that isn't null under its output folder, plus the probe's module sizes when given, and runs
 * the script there.
 * @param {{ dist: Manifest | null, probe: Manifest | null, modules?: Record<string, Record<string, number>> }} manifests
 */
const spawn = ({ dist, probe, modules }) => {
  const dir = mkdtempSync(join(tmpdir(), "orvano-size-"));
  if (modules) {
    mkdirSync(join(dir, "dist-size-probe", ".vite"), { recursive: true });
    writeFileSync(join(dir, "dist-size-probe", ".vite", "modules.json"), JSON.stringify(modules));
  }
  for (const [outDir, contents] of /** @type {const} */ ([
    ["dist", dist],
    ["dist-size-probe", probe],
  ])) {
    if (!contents) continue;
    mkdirSync(join(dir, outDir, ".vite"), { recursive: true });
    writeFileSync(join(dir, outDir, ".vite", "manifest.json"), JSON.stringify(contents));
  }
  return { dir, result: spawnSync("node", [script], { cwd: dir, encoding: "utf8" }) };
};

/**
 * @param {Manifest} manifest
 * @param {Manifest} [probe]
 * @returns {{ status: number | null, stdout: string, entries: Entry[] }}
 */
const run = (manifest, probe = probeManifest) => {
  const { dir, result } = spawn({ dist: manifest, probe });
  try {
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

// The probe build: the same route files, with AppShell in the welcome page (STY-9).
/** @type {Manifest} */
const probeManifest = {
  "index.html": { file: "assets/index-p1.js", isEntry: true, imports: ["_ui-p2.js"] },
  "_ui-p2.js": { file: "assets/ui-p2.js" },
  "src/routes/__root.tsx": { file: "assets/root-p3.js", imports: ["_ui-p2.js"] },
  "src/routes/_shell/index.tsx?tsr-split=component": {
    file: "assets/index-route-p4.js",
    imports: ["_shell-p5.js"],
    dynamicImports: ["_palette-p6.js"],
  },
  "_shell-p5.js": { file: "assets/shell-p5.js" },
  "_palette-p6.js": { file: "assets/palette-p6.js" },
  "src/routes/about.tsx": { file: "assets/about-p7.js" },
  "style.css": { file: "assets/style-p8.css", isEntry: true },
};

/** The entries after the app's CSS entry: the probe's first load, then its per file entries. */
const probeEntries = (/** @type {Entry[]} */ entries) =>
  entries.slice(entries.findIndex((entry) => entry.name === "CSS (STY-4)") + 1);

describe("size-entries.mjs", () => {
  test("the first load holds the entry, first load routes, the en catalog, and their static imports", () => {
    const [firstLoad] = run(manifest).entries;
    assert.equal(firstLoad?.name, "First load (BDL-1)");
    assert.equal(firstLoad.limit, "230 KB");
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
    const { entries } = run(manifestWithDuplicate);
    const perFile = entries.slice(
      1,
      entries.findIndex((entry) => entry.name === "CSS (STY-4)"),
    );
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

  test("0015 AC-3: the stylesheet gets one 30 KB entry, after the app's files", () => {
    const { entries } = run(manifest);
    const css = entries.find((entry) => entry.name === "CSS (STY-4)");
    assert.equal(entries.indexOf(/** @type {Entry} */ (css)), 12);
    assert.deepEqual(css, {
      name: "CSS (STY-4)",
      path: ["dist/assets/style-j0.css"],
      limit: "30 KB",
      gzip: true,
    });
  });

  test("0015 STY-9: the shell probe's first load holds the entry, root and index routes, and their static imports", () => {
    const [shell] = probeEntries(run(manifest).entries);
    assert.equal(shell?.name, "Shell first load (AC-3, probe)");
    assert.equal(shell.limit, "230 KB");
    assert.equal(shell.gzip, true);
    assert.deepEqual([.../** @type {string[]} */ (shell.path)].toSorted(), [
      "dist-size-probe/assets/index-p1.js",
      "dist-size-probe/assets/index-route-p4.js",
      "dist-size-probe/assets/root-p3.js",
      "dist-size-probe/assets/shell-p5.js",
      "dist-size-probe/assets/ui-p2.js",
    ]);
  });

  test("0015 STY-9: every probe JS file gets its own 80 KB entry, named by its probe path", () => {
    const perFile = probeEntries(run(manifest).entries).slice(1);
    assert.deepEqual(
      perFile.map((entry) => entry.path),
      [
        "dist-size-probe/assets/about-p7.js",
        "dist-size-probe/assets/index-p1.js",
        "dist-size-probe/assets/index-route-p4.js",
        "dist-size-probe/assets/palette-p6.js",
        "dist-size-probe/assets/root-p3.js",
        "dist-size-probe/assets/shell-p5.js",
        "dist-size-probe/assets/ui-p2.js",
      ],
    );
    for (const entry of perFile) {
      assert.equal(entry.limit, "80 KB");
      assert.equal(entry.name, `${String(entry.path)} (BDL-1, one file)`);
    }
  });

  for (const [missing, path] of /** @type {const} */ ([
    ["dist", "dist/.vite/manifest.json"],
    ["probe", "dist-size-probe/.vite/manifest.json"],
  ])) {
    test(`fails naming ${path} when it is missing`, () => {
      const { dir, result } = spawn({
        dist: missing === "dist" ? null : manifest,
        probe: missing === "probe" ? null : probeManifest,
      });
      try {
        assert.equal(result.status, 1);
        assert.match(result.stderr, new RegExp(`${path.replaceAll(".", "\\.")} is missing`));
        assert.ok(!existsSync(join(dir, ".size-limit.json")));
      } finally {
        rmSync(dir, { recursive: true, force: true });
      }
    });
  }

  test("import cycles don't loop", () => {
    const { entries } = run({
      "index.html": { file: "assets/a.js", isEntry: true, imports: ["_b.js"] },
      "_b.js": { file: "assets/b.js", imports: ["index.html"] },
    });
    assert.deepEqual(entries[0]?.path, ["dist/assets/a.js", "dist/assets/b.js"]);
  });

  test("0015 AC-3: prints and saves the ten largest modules of the shell's first load", () => {
    /** @type {Record<string, Record<string, number>>} */
    const modules = {
      "assets/ui-p2.js": Object.fromEntries(
        Array.from({ length: 11 }, (_, index) => [`../src/ui/m${index}.tsx`, 100 + index]),
      ),
      "assets/shell-p5.js": {
        "../node_modules/.pnpm/react-aria@3.0.0/node_modules/react-aria/dist/big.js": 5000,
      },
      // Not in the shell's first load.
      "assets/palette-p6.js": { "../src/ui/palette.tsx": 9000 },
    };
    const { dir, result } = spawn({ dist: manifest, probe: probeManifest, modules });
    try {
      assert.equal(result.status, 0, result.stderr);
      const saved = readFileSync(join(dir, "dist-size-probe", "shell-largest-modules.txt"), "utf8");
      const lines = saved.trim().split("\n");
      assert.match(lines[0] ?? "", /ten largest modules of the shell's first load/);
      assert.equal(lines.length, 11);
      assert.match(
        lines[1] ?? "",
        /^\s+5000 {2}react-aria\/dist\/big\.js {2}\(assets\/shell-p5\.js\)$/,
      );
      assert.match(lines[2] ?? "", /110 {2}\.\.\/src\/ui\/m10\.tsx/);
      assert.ok(!saved.includes("palette"));
      assert.ok(!saved.includes("m0.tsx"));
      assert.ok(result.stdout.includes(saved.trim()));
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  test("says so when the probe build has no module sizes", () => {
    assert.match(run(manifest).stdout, /no dist-size-probe\/\.vite\/modules\.json/);
  });

  test("reports how many entries and first load files it wrote", () => {
    assert.match(
      run(manifest).stdout,
      /size-entries: 21 entries \(9 first load files, 6 probe first load files\)/,
    );
  });
});
