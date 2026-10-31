import { createRequire } from "node:module";
import { expect, test, type Page } from "@playwright/test";

// Spec 0016 AC-5, AC-8, FRS-5 to FRS-7: the built console under `vite preview` (with spec 0006 NET-2's CSP), from
// /console/ to the accepted setup token. vite preview has no API, so the two calls are answered here; the real server
// path is covered by the install smoke test (SMK-3) and the integration tests.

const TOKEN = "orv_setup_dGhpcyBpcyBhIHRlc3QgdG9rZW4gb2YgMzIgYnl0ZXM";
const axeSource = createRequire(import.meta.url).resolve("axe-core/axe.min.js");

/** Answers boot's config call with setup open, and the token check with `status`; returns the tokens sent. */
async function mockSetupApi(page: Page, status = 204) {
  const sent: string[] = [];
  await page.route("**/v1/console/config", (route) =>
    route.fulfill({ json: { setupRequired: true }, headers: { "Cache-Control": "no-store" } }),
  );
  await page.route("**/v1/console/setup/token", async (route) => {
    sent.push((route.request().postDataJSON() as { token: string }).token);
    await (status === 204
      ? route.fulfill({ status: 204 })
      : route.fulfill({
          status,
          contentType: "application/problem+json",
          json: {
            type: "about:blank",
            title: "Unauthorized",
            status,
            detail: "The setup token is not valid.",
            code: "setup_token_invalid",
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
          },
        }));
  });
  return sent;
}

/** Collects CSP violations for the page's life. */
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
  return violations;
}

test.describe("first run setup", () => {
  test("0016 AC-5, AC-8: /console/ lands on setup, and a typed token reaches the accepted state", async ({
    page,
  }) => {
    const sent = await mockSetupApi(page);
    const violations = await watchViolations(page);

    await page.goto("./");
    await expect(page).toHaveURL(/\/console\/setup$/);
    await expect(page.getByRole("heading", { level: 1, name: "Set up Orvano" })).toBeVisible();

    await page.getByRole("textbox", { name: "Setup token" }).fill(TOKEN);
    await page.getByRole("button", { name: "Continue" }).click();

    await expect(
      page.getByRole("heading", { level: 2, name: "Setup token accepted" }),
    ).toBeFocused();
    expect(sent).toEqual([TOKEN]);
    expect(violations).toEqual([]);
  });

  test("0016 FRS-5: the setup link's token is checked, then gone from the address, history, and storage", async ({
    page,
  }) => {
    const sent = await mockSetupApi(page);
    const requests: string[] = [];
    page.on("request", (request) => requests.push(request.url()));

    await page.goto(`setup#token=${TOKEN}`);

    await expect(
      page.getByRole("heading", { level: 2, name: "Setup token accepted" }),
    ).toBeVisible();
    expect(sent).toEqual([TOKEN]);
    expect(new URL(page.url()).hash).toBe("");
    // The fragment never reaches a server: no request URL holds the token.
    expect(requests.filter((url) => url.includes(TOKEN))).toEqual([]);
    const stored = await page.evaluate(() =>
      JSON.stringify({ ...localStorage, ...sessionStorage }),
    );
    expect(stored).not.toContain(TOKEN);

    // VFY-2: a reload shows the empty step, with no token in the address to check again.
    await page.reload();
    await expect(page.getByRole("textbox", { name: "Setup token" })).toHaveValue("");
    expect(new URL(page.url()).hash).toBe("");
    expect(sent).toEqual([TOKEN]);
  });

  test("0016 FRS-5: a new link opened on the accepted page leaves the address and replaces the token", async ({
    page,
  }) => {
    const sent = await mockSetupApi(page);
    await page.goto(`setup#token=${TOKEN}`);
    const accepted = page.getByRole("heading", { level: 2, name: "Setup token accepted" });
    await expect(accepted).toBeVisible();

    // Only the fragment differs, so this is the same document, as when a link lands in an open tab.
    await page.goto(`setup#token=${TOKEN}2`);

    await expect(page).toHaveURL(/\/console\/setup$/);
    await expect.poll(() => sent).toEqual([TOKEN, `${TOKEN}2`]);
    await expect(accepted).toBeFocused();
  });

  test("0016 FRS-6: a rejected link token stays in the field with the error", async ({ page }) => {
    await mockSetupApi(page, 401);

    await page.goto(`setup#token=${TOKEN}`);

    const field = page.getByRole("textbox", { name: "Setup token" });
    await expect(field).toHaveAttribute("aria-invalid", "true");
    await expect(field).toHaveValue(TOKEN);
    await expect(
      page.getByText(/not valid any more\. Make a new one with orvano admin setup-token\./).first(),
    ).toBeVisible();
    expect(new URL(page.url()).hash).toBe("");
  });
});

test.describe("first run boot", () => {
  test("0016 FRS-3, URL-5: an old setup link after setup is done lands on the console with a clean address", async ({
    page,
  }) => {
    const sent: string[] = [];
    await page.route("**/v1/console/config", (route) =>
      route.fulfill({ json: { setupRequired: false }, headers: { "Cache-Control": "no-store" } }),
    );
    await page.route("**/v1/console/setup/token", (route) => {
      sent.push(route.request().url());
      return route.abort();
    });

    await page.goto(`setup#token=${TOKEN}`);

    await expect(page.getByRole("heading", { level: 1, name: "Orvano console" })).toBeVisible();
    expect(page.url()).not.toContain(TOKEN);
    expect(sent).toEqual([]);
  });

  test("0016 FRS-3: /console/ with a token still lands on setup, and leaves no token behind", async ({
    page,
  }) => {
    await mockSetupApi(page);

    await page.goto(`./#token=${TOKEN}`);

    await expect(page.getByRole("heading", { level: 1, name: "Set up Orvano" })).toBeVisible();
    await expect(page).toHaveURL(/\/console\/setup/);
    expect(page.url()).not.toContain(TOKEN);
  });

  test("0016 FRS-2: a failed config load shows the boot error page, and Reload tries again", async ({
    page,
  }) => {
    let failing = true;
    await page.route("**/v1/console/config", (route) =>
      failing
        ? route.fulfill({ status: 500, body: "no" })
        : route.fulfill({ json: { setupRequired: true } }),
    );

    await page.goto("./");

    await expect(
      page.getByRole("heading", { level: 1, name: "Orvano couldn't start" }),
    ).toBeVisible();
    failing = false;
    await page.getByRole("button", { name: "Reload" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Set up Orvano" })).toBeVisible();
  });

  test("0016 FRS-2: a config call that never connects shows the boot error page", async ({
    page,
  }) => {
    await page.route("**/v1/console/config", (route) => route.abort());

    await page.goto("setup");

    await expect(
      page.getByRole("heading", { level: 1, name: "Orvano couldn't start" }),
    ).toBeVisible();
  });
});

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
    // React Aria's live announcer keeps a pending button's announcement (by `aria-labelledby`) for 7 seconds, and
    // once the form is gone that entry points at nothing (src/ui/announcer.ts); the page itself is what's checked.
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

// Spec 0016 FRS-7, spec 0003 QA-5: axe (WCAG tags plus best-practice) on the built page in both themes. axe is
// injected as a script, which the CSP would refuse, so these tests bypass it; the tests above keep it on.
test.describe("first run setup accessibility", () => {
  test.use({ bypassCSP: true });

  for (const colorScheme of ["light", "dark"] as const) {
    test(`0016 FRS-7: each setup state passes axe in the ${colorScheme} theme`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme });
      await mockSetupApi(page, 401);

      await page.goto("setup");
      await expect(page.locator("html")).toHaveAttribute("data-theme", colorScheme);
      await expect(page.getByRole("textbox", { name: "Setup token" })).toBeVisible();
      await expectNoAxeViolations(page);

      await page.getByRole("textbox", { name: "Setup token" }).fill(TOKEN);
      await page.getByRole("button", { name: "Continue" }).click();
      await expect(page.getByRole("textbox", { name: "Setup token" })).toHaveAttribute(
        "aria-invalid",
        "true",
      );
      await expectNoAxeViolations(page);

      // A new token (the rejected one would only show its error again) is accepted.
      await page.unroute("**/v1/console/setup/token");
      await page.route("**/v1/console/setup/token", (route) => route.fulfill({ status: 204 }));
      await page.getByRole("textbox", { name: "Setup token" }).fill(`${TOKEN}2`);
      await page.getByRole("button", { name: "Continue" }).click();
      await expect(
        page.getByRole("heading", { level: 2, name: "Setup token accepted" }),
      ).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});
