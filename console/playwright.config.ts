import { defineConfig } from '@playwright/test'

// End to end tests against the production shape: the gateway (Caddy plus the console build) in
// front of the scenario server, started with
//   docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['list']] : 'list',
  use: {
    baseURL: process.env.CONSOLE_URL ?? 'http://localhost:8081',
    trace: 'retain-on-failure',
    // A person's first visit: the operating system prefers light, and the console still opens dark.
    colorScheme: 'light',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
})
