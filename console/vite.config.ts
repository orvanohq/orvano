import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react, { reactCompilerPreset } from "@vitejs/plugin-react";
import babel from "@rolldown/plugin-babel";
import { tanstackRouter } from "@tanstack/router-plugin/vite";
import { lingui } from "@lingui/vite-plugin";
import tailwindcss from "@tailwindcss/vite";

// Spec 0006 NET-2, the exact policy the server sends (server/src/Orvano.Host/Web/SecurityHeaders.cs). `vite preview`
// sends it too, so the CSP test (spec 0015 STY-6) loads the build under the real policy.
const consolePolicy =
  "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; " +
  "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

export default defineConfig({
  plugins: [
    tanstackRouter({ target: "react", autoCodeSplitting: true }), // spec 0003 RTE-1: before react()
    react(),
    lingui(), // spec 0003 I18N-2: .po files compile on import
    babel({
      // spec 0003 WEB-6, I18N-2: one pass. Babel runs plugins before presets, so the macros expand
      // before the React Compiler reads the code.
      plugins: ["@lingui/babel-plugin-lingui-macro"],
      presets: [reactCompilerPreset()],
    }),
    tailwindcss(), // spec 0015 STY-1: the one stylesheet, src/ui/theme.css
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
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  build: {
    manifest: true, // spec 0003 BDL-1
    cssCodeSplit: false, // spec 0015 STY-2: one CSS file for the whole console
    rolldownOptions: {
      output: {
        // Spec 0003 BDL-1: no emitted file over 80 KB gzip, so the framework libraries get their own chunks.
        codeSplitting: {
          groups: [
            { name: "react", test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: "tanstack", test: /node_modules[\\/]@tanstack[\\/]/ },
            { name: "lingui", test: /node_modules[\\/]@lingui[\\/]/ },
            {
              name: "react-aria",
              test: /node_modules[\\/](react-aria-components|react-aria|react-stately|@react-aria|@react-stately|@internationalized|@react-types)[\\/]/,
            },
          ],
        },
      },
    },
  },
});
