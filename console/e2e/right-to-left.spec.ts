import { createRequire } from "node:module";
import { expect, test, type Page } from "@playwright/test";
import { mockConsoleApi, mockSignedOut } from "./mocks";
import { landingOf, routePaths, urlOf } from "./routes";

const axeSource = createRequire(import.meta.url).resolve("axe-core/axe.min.js");

/** Spec 0003 QA-5: axe with the WCAG tags plus best-practice, on the whole page. */
async function expectNoAxeViolations(page: Page) {
  await page.addScriptTag({ path: axeSource });
  const violations = await page.evaluate(async () => {
    const axe = (
      window as unknown as {
        axe: {
          run: (
            context: unknown,
            options: unknown,
          ) => Promise<{ violations: { id: string; nodes: { target: string[] }[] }[] }>;
        };
      }
    ).axe;
    const results = await axe.run(
      { include: [["body"]], exclude: [["[data-live-announcer]"]] },
      {
        runOnly: {
          type: "tag",
          values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa", "best-practice"],
        },
      },
    );
    return results.violations.map(
      (violation) =>
        `${violation.id}: ${violation.nodes.map((node) => node.target.join(" ")).join(", ")}`,
    );
  });
  expect(violations).toEqual([]);
}

async function mockConfig(page: Page, setupRequired: boolean) {
  await page.route("**/v1/console/config", (route) =>
    route.fulfill({
      json: {
        setupRequired,
        instanceName: "Orvano",
        publicUrl: "http://localhost:4173",
        features: [],
      },
    }),
  );
}

// Spec 0017 LOC-5, AC-23: the built stylesheet right to left, on every route, in both themes. Only English ships, so
// this sets `dir` on the loaded page, which flips the layout; right-to-left-pages.test.tsx proves the rest of RTL-1 with
// React Aria's `ar` locale. axe is injected as a script, which the CSP would refuse, so these tests bypass it; csp.spec.ts
// keeps it on.
test.describe("right to left", () => {
  test.use({ bypassCSP: true });

  for (const colorScheme of ["light", "dark"] as const) {
    test.describe(`in the ${colorScheme} theme`, () => {
      test.use({ colorScheme });

      for (const path of routePaths()) {
        test(`0017 LOC-5: ${path} lays out right to left and passes axe`, async ({
          page,
          baseURL,
        }) => {
          if (["/setup", "/sign-in", "/reset"].includes(path)) await mockSignedOut(page);
          else await mockConsoleApi(page);
          await mockConfig(page, path === "/setup");

          await page.goto(urlOf(path).slice(1));
          await expect(page).toHaveURL(new URL(landingOf(path).slice(1), baseURL).href);
          await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
          await page.evaluate(() => {
            document.documentElement.dir = "rtl";
          });

          await expect(page.locator("html")).toHaveAttribute("data-theme", colorScheme);
          const sideways = await page.evaluate(() => {
            const root = document.documentElement;
            return root.scrollWidth > root.clientWidth;
          });
          expect(sideways).toBe(false);
          await expectNoAxeViolations(page);
        });
      }
    });
  }
});
