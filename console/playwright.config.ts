import { defineConfig, devices } from "@playwright/test";

// Spec 0015 STY-6 and the theme boot scenario: static checks against the built console under `vite preview`, which
// sends spec 0006 NET-2's CSP (vite.config.ts). Run `pnpm build` first. Spec 0003 QA-5's full stack run (a real server
// and PostgreSQL) has its own config, playwright.stack.config.ts.
export default defineConfig({
  testDir: "e2e",
  testIgnore: "stack/**",
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: "http://localhost:4173/console/",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "pnpm exec vite preview",
    url: "http://localhost:4173/console/",
    reuseExistingServer: !process.env.CI,
  },
});
