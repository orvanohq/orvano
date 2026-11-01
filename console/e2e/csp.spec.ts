import { expect, test, type Page } from "@playwright/test";
import { mockConsoleApi, mockSignedOut } from "./mocks";
import { landingOf, routePaths, urlOf } from "./routes";

// Spec 0015 STY-6: the built console, served with spec 0006 NET-2's exact CSP, loads every route with no violation.
// vite.config.ts sends that policy, and tooling/scripts/console-csp.test.mjs keeps it equal to the server's.

const paths = routePaths();

test("0015 AC-23: the route list comes from the route tree", () => {
  expect(paths).toContain("/");
  // Spec 0016 FRS-7: the first run setup page loads under the policy too.
  expect(paths).toContain("/setup");
  // Spec 0017 CNS-8: every route of CNS-1.
  for (const path of [
    "/sign-in",
    "/reset",
    "/account",
    "/$orgId",
    "/$orgId/settings",
    "/$orgId/$projectId",
    "/$orgId/$projectId/settings/general",
    "/$orgId/$projectId/settings/keys",
    "/$orgId/$projectId/settings/origins",
  ]) {
    expect(paths).toContain(path);
  }
});

/** Collects what the policy refused, from the event and from the console. */
async function watchViolations(page: Page) {
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
  return violations;
}

for (const path of paths) {
  test(`0015 AC-23: ${path} loads under the console CSP with no violation`, async ({
    page,
    baseURL,
  }) => {
    const response = await page.request.get(path.slice(1));
    expect(response.headers()["content-security-policy"]).toContain("script-src 'self'");

    const violations = await watchViolations(page);

    // vite preview has no API. Setup and sign in render signed out; every other route needs the session (spec 0017 CNS-2).
    const signedIn = !["/setup", "/sign-in", "/reset"].includes(path);
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
    await page.goto(urlOf(path).slice(1));
    await expect(page).toHaveURL(new URL(landingOf(path).slice(1), baseURL).href);
    await expect(page.locator("main")).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme", /^(light|dark)$/);
    expect(violations).toEqual([]);
  });
}

// Spec 0015 STY-6: what opens over a page, under the policy: a menu, and a lazy dialog.
test("0015 AC-23: overlays open under the console CSP with no violation", async ({ page }) => {
  const violations = await watchViolations(page);
  await mockConsoleApi(page);
  await page.route("**/v1/console/config", (route) =>
    route.fulfill({
      json: {
        setupRequired: false,
        instanceName: "Orvano",
        publicUrl: "http://localhost:4173",
        features: [],
      },
    }),
  );

  await page.goto(urlOf("/$orgId/$projectId/settings/keys").slice(1));
  await page.getByRole("button", { name: "Account" }).click();
  await expect(page.getByRole("menu")).toBeVisible();
  await page.keyboard.press("Escape");

  await page.getByRole("button", { name: "Commands" }).click();
  await expect(page.getByRole("dialog", { name: "Command palette" })).toBeVisible();

  expect(violations).toEqual([]);
});
