import { relative } from "node:path";
import { fileURLToPath, URL } from "node:url";
import { defineConfig, type Plugin } from "vite";
import react, { reactCompilerPreset } from "@vitejs/plugin-react";
import babel from "@rolldown/plugin-babel";
import { tanstackRouter } from "@tanstack/router-plugin/vite";
import { lingui } from "@lingui/vite-plugin";
import tailwindcss from "@tailwindcss/vite";
import optimizeLocales from "@react-aria/optimize-locales-plugin";
import { reactAriaLocales } from "./src/shared/i18n/locales";

// Spec 0006 NET-2, the exact policy the server sends (server/src/Orvano.Host/Web/SecurityHeaders.cs). `vite preview`
// sends it too, so the CSP test (spec 0015 STY-6) loads the build under the real policy. A copy, so
// tooling/scripts/console-csp.test.mjs fails when it differs from the server's.
const consolePolicy =
  "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; " +
  "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

// Spec 0015 STY-9, AC-3: the probe build writes each chunk's modules with their rendered size (before minify), so
// `pnpm size` can name the largest modules of the shell's first load.
const moduleSizes: Plugin = {
  name: "orvano:module-sizes",
  generateBundle(_options, bundle) {
    const root = fileURLToPath(new URL(".", import.meta.url));
    const chunks = Object.fromEntries(
      Object.values(bundle).flatMap((output) =>
        output.type === "chunk"
          ? [
              [
                output.fileName,
                Object.fromEntries(
                  Object.entries(output.modules).map(([id, module]) => [
                    relative(root, id),
                    module.renderedLength,
                  ]),
                ),
              ],
            ]
          : [],
      ),
    );
    this.emitFile({
      type: "asset",
      fileName: ".vite/modules.json",
      source: `${JSON.stringify(chunks, null, 2)}\n`,
    });
  },
};

export default defineConfig(({ mode }) => ({
  plugins: [
    tanstackRouter({ target: "react", autoCodeSplitting: true }), // spec 0003 RTE-1: before react()
    // Spec 0003 I18N-6: React Aria's own strings only for the shipped locales; the rest fall back to en-US.
    { ...optimizeLocales.vite({ locales: reactAriaLocales }), enforce: "pre" },
    react(),
    lingui(), // spec 0003 I18N-2: .po files compile on import
    babel({
      // spec 0003 WEB-6, I18N-2: one pass. Babel runs plugins before presets, so the macros expand
      // before the React Compiler reads the code.
      plugins: ["@lingui/babel-plugin-lingui-macro"],
      presets: [reactCompilerPreset()],
    }),
    tailwindcss(), // spec 0015 STY-1: the one stylesheet, src/ui/theme.css
    ...(mode === "size-probe" ? [moduleSizes] : []),
  ],
  base: "/console/", // spec 0003 WEB-8, spec 0007 CSL-1
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      // spec 0007 CSL-3: every path outside /console goes to the server, so the browser sees one origin.
      "^/(?!console(/|$))": { target: "http://localhost:5080" },
    },
  },
  preview: {
    port: 4173,
    strictPort: true,
    headers: { "Content-Security-Policy": consolePolicy, "Referrer-Policy": "same-origin" },
  },
  resolve: {
    // Spec 0015 STY-9: the size probe build renders AppShell in place of the `_shell` index page, so `pnpm size` measures
    // the shell's first load. The route files stay the same, so src/routeTree.gen.ts doesn't change.
    alias: [
      ...(mode === "size-probe"
        ? [
            {
              find: "@/features/shell/components/no-organization",
              replacement: fileURLToPath(
                new URL("./src/size-probe/shell-probe.tsx", import.meta.url),
              ),
            },
          ]
        : []),
      { find: "@", replacement: fileURLToPath(new URL("./src", import.meta.url)) },
    ],
  },
  build: {
    manifest: true, // spec 0003 BDL-1
    cssCodeSplit: false, // spec 0015 STY-2: one CSS file for the whole console
    rolldownOptions: {
      output: {
        // Spec 0003 BDL-1: no emitted file over 80 KB gzip, so the framework libraries get their own chunks. React Aria
        // has no group: a group gathers every module it matches into one chunk, so the parts only the lazy panels use
        // (the palette's Autocomplete and Menu, PRM-10) would load first. Split by importer, its first load parts stay
        // well under 80 KB.
        codeSplitting: {
          groups: [
            { name: "react", test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            // TanStack Form (and the two helpers only it uses) stays out of this group, which loads first: it goes
            // with the screens that have a form, so the shell's first load stays in AC-3's budget. So does useMutation:
            // only those screens call it.
            {
              name: "tanstack",
              test: /node_modules[\\/]@tanstack[\\/](?!react-form|form-core|pacer-lite|devtools-event-client|react-query[\\/]build[\\/]modern[\\/]useMutation|query-core[\\/]build[\\/]modern[\\/]mutationObserver)/,
            },
            // Spec 0015 STY-8: what the shell's first load shares. Left to the importer split, the routes cut these into
            // two dozen small files, and each compresses alone, which costs more than 10 KB of BDL-1. What only later pages
            // use stays out: the lazy panels, the form, the table, and the primitives no first load page renders.
            {
              name: "shell-core",
              maxSize: 300_000, // bytes before gzip, which keeps each part under BDL-1's 80 KB file limit
              test: /src[\\/](ui[\\/](?!command-palette|docs-pane|shortcut-help|confirm-delete|side-sheet|table|form[\\/]|tabs|relative-time|status-chip|progress)|shared[\\/])/,
            },
            // Every route's loader reads one of these, so they load together rather than as one small file each.
            { name: "queries", test: /src[\\/]features[\\/][^\\/]+[\\/]api[\\/]queries\.ts/ },
            { name: "lingui", test: /node_modules[\\/]@lingui[\\/]/ },
          ],
        },
      },
    },
  },
}));
