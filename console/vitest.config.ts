import { defineConfig, mergeConfig } from "vitest/config";
import { playwright } from "@vitest/browser-playwright";
import { msw } from "msw/vite";
import type { BrowserCommand } from "vitest/node";
import viteConfig from "./vite.config";

// Spec 0003 QA-1: browser mode only, on the app's own Vite config, so tests run the same router plugin, Lingui
// macros, and React Compiler as the app: features and shared code in Chromium, src/ui primitives in Chromium,
// Firefox, and WebKit.
// The dev proxy (spec 0007 CSL-3) is left out: it would send Vitest's own browser requests to the server. MSW's Vite
// plugin serves its worker script to the tests only (spec 0003 QA-2), so the build never has it.
// vite.config.ts is a function of the mode (spec 0015 STY-9); tests take its default, non probe config.
const appConfig = viteConfig({ command: "serve", mode: "test" });

// Spec 0015 AC-6: forced colors mode, through Playwright's media emulation (src/test/media.ts calls it).
const emulateForcedColors: BrowserCommand<[active: boolean]> = async ({ page }, active) => {
  await page.emulateMedia({ forcedColors: active ? "active" : "none" });
};

export default mergeConfig(
  {
    ...appConfig,
    plugins: [...(appConfig.plugins ?? []), msw({ mode: "worker-only" })],
    server: { ...appConfig.server, proxy: undefined },
    // Vite finds this one late on a cold cache, and the reload that follows fails whichever test file is loading.
    optimizeDeps: { include: ["react-dom/client"] },
  },
  defineConfig({
    test: {
      // Spec 0008 CI-6: coverage runs with --coverage (CI), and is reported, never gated.
      coverage: {
        provider: "v8",
        include: ["src/**/*.{ts,tsx}"],
        exclude: [
          "src/**/*.test.{ts,tsx}",
          "src/test/**",
          "src/routeTree.gen.ts",
          "src/locales/**",
          "src/gallery/**",
        ],
        reporter: ["text", "json-summary"],
      },
      projects: [
        {
          extends: true,
          test: {
            name: "app",
            include: ["src/**/*.test.{ts,tsx}"],
            exclude: ["src/ui/**", "src/gallery/**"],
            // Features and shared code call the API, so they run against MSW (QA-2); primitives never do.
            setupFiles: ["src/test/setup.ts", "src/test/setup-msw.ts"],
            browser: {
              enabled: true,
              headless: true,
              provider: playwright(),
              instances: [{ browser: "chromium" }],
            },
          },
        },
        {
          extends: true,
          test: {
            name: "ui",
            // The gallery's scene tests check the shell as a whole, so they run in all three engines too.
            include: ["src/ui/**/*.test.{ts,tsx}", "src/gallery/**/*.test.{ts,tsx}"],
            setupFiles: ["src/test/setup.ts"],
            browser: {
              enabled: true,
              headless: true,
              provider: playwright(),
              instances: [{ browser: "chromium" }, { browser: "firefox" }, { browser: "webkit" }],
              commands: { emulateForcedColors },
            },
          },
        },
      ],
    },
  }),
);
