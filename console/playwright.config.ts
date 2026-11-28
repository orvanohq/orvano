import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright and axe against the built export (spec 0003, "Tests"). Build first:
 *   pnpm --filter @orvano/console build && pnpm --filter @orvano/console test:e2e
 * The web server builds nothing; it serves console/out at /console with the host's rules.
 * Chromium only for now; Firefox and WebKit arrive with feature 89.
 */
export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: "http://localhost:3100/console/",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "node scripts/serve-export.mjs --port 3100",
    url: "http://localhost:3100/console/",
    reuseExistingServer: !process.env.CI,
  },
});
