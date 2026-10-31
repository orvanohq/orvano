import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import { mockConsoleApi, mockSignedOut, ORG_ID, PROJECT_ID } from "./mocks";

// Spec 0015 STY-6: the built console, served with spec 0006 NET-2's exact CSP, loads every route with no violation.
// vite.config.ts sends that policy, and tooling/scripts/console-csp.test.mjs keeps it equal to the server's. Opening
// overlays (a menu, a dialog, a toast) under the policy joins here with feature 13: no route the build has hosts one.

/** Every route the build has, read from the generated route tree, so a new route joins this test by itself. */
function routePaths(): string[] {
  const tree = readFileSync(new URL("../src/routeTree.gen.ts", import.meta.url), "utf8");
  const union = /fullPaths:\s*([^\n]+(?:\n\s*\|[^\n]+)*)/.exec(tree)?.[1] ?? "";
  return [...union.matchAll(/'([^']+)'/g)].map((match) => match[1] ?? "");
}

const paths = [
  ...new Set(routePaths().map((path) => (path === "/" ? path : path.replace(/\/$/, "")))),
];

test("0015 AC-23: the route list comes from the route tree", () => {
  expect(paths).toContain("/");
  // Spec 0016 FRS-7: the first run setup page loads under the policy too.
  expect(paths).toContain("/setup");
});

for (const path of paths) {
  test(`0015 AC-23: ${path} loads under the console CSP with no violation`, async ({ page }) => {
    // Feature 13's shell routes take params; give them sample values here when they land.

    const response = await page.request.get(path.slice(1));
    expect(response.headers()["content-security-policy"]).toContain("script-src 'self'");

    const violations: string[] = [];
    await page.exposeFunction("reportViolation", (text: string) => violations.push(text));
    await page.addInitScript(() => {
      document.addEventListener("securitypolicyviolation", (event) => {
        void (
          window as unknown as { reportViolation: (text: string) => Promise<void> }
        ).reportViolation(`${event.violatedDirective} blocked ${event.blockedURI}`);
      });
    });
    page.on("console", (message) => {
      if (message.type() === "error" && message.text().includes("Content Security Policy")) {
        violations.push(message.text());
      }
    });

    // vite preview has no API. Setup and sign in render signed out; every other route needs the session (spec 0017 CNS-2).
    const signedIn = !["/setup", "/sign-in"].includes(path);
    if (signedIn) await mockConsoleApi(page);
    else await mockSignedOut(page);
    await page.route("**/v1/console/config", (route) =>
      route.fulfill({
        json: {
          setupRequired: path === "/setup",
          instanceName: "Orvano",
          publicUrl: "http://localhost:4173",
          features: [],
        },
      }),
    );
    const url = path.replace("$orgId", ORG_ID).replace("$projectId", PROJECT_ID);
    await page.goto(url.slice(1));
    const landing = path === "/" ? `/${ORG_ID}` : url.replace(/\/settings$/, "/settings/keys");
    await expect(page).toHaveURL(new URL(landing.slice(1), "http://localhost:4173/console/").href);
    await expect(page.locator("main")).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme", /^(light|dark)$/);
    expect(violations).toEqual([]);
  });
}
