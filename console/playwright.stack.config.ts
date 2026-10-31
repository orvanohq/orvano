import { defineConfig, devices } from "@playwright/test";

// Spec 0003 QA-5: Playwright on the built console, served by the real image with PostgreSQL in containers. It needs the
// image first: `docker build --file deploy/Dockerfile --build-arg ORVANO_IMAGE_VERSION=ci --tag orvano:ci .` from the repo root. The run shares one install,
// so it has one worker.
export default defineConfig({
  testDir: "e2e/stack",
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? "github" : "list",
  workers: 1,
  timeout: 90_000,
  globalSetup: "./e2e/stack/global-setup.ts",
  use: {
    baseURL: "http://localhost:18090/",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
