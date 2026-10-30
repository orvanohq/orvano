import { readFileSync } from "node:fs";
import { expect, test } from "@playwright/test";

// Spec 0015 STY-6: the built console, served with spec 0006 NET-2's exact CSP, loads every route with no violation.
// vite.config.ts sends that policy, and tooling/scripts/console-csp.test.mjs keeps it equal to the server's. Opening
// overlays (a menu, a dialog, a toast) under the policy joins here with feature 13: no route the build has hosts one.

/** Every route the build has, read from the generated route tree, so a new route joins this test by itself. */
function routePaths(): string[] {
  const tree = readFileSync(new URL("../src/routeTree.gen.ts", import.meta.url), "utf8");
  const union = /fullPaths:\s*([^\n]+(?:\n\s*\|[^\n]+)*)/.exec(tree)?.[1] ?? "";
  return [...union.matchAll(/'([^']+)'/g)].map((match) => match[1] ?? "");
}

const paths = routePaths();

test("0015 AC-23: the route list comes from the route tree", () => {
  expect(paths).toContain("/");
  // Spec 0016 FRS-7: the first run setup page loads under the policy too.
  expect(paths).toContain("/setup");
});

for (const path of paths) {
  test(`0015 AC-23: ${path} loads under the console CSP with no violation`, async ({ page }) => {
    // Feature 13's shell routes take params; give them sample values here when they land.
    expect(path, "add sample params for this route").not.toContain("$");

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

    // vite preview has no API: answer boot's config call (spec 0016 FRS-2) so the route itself renders, not a redirect.
    await page.route("**/v1/console/config", (route) =>
      route.fulfill({ json: { setupRequired: path === "/setup" } }),
    );
    await page.goto(path.slice(1));
    await expect(page).toHaveURL(new URL(path.slice(1), "http://localhost:4173/console/").href);
    await expect(page.locator("main")).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme", /^(light|dark)$/);
    expect(violations).toEqual([]);
  });
}
