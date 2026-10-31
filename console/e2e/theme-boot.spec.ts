import { expect, test } from "@playwright/test";
import { mockSignedOut } from "./mocks";

// Spec 0015 THM-1 to THM-5: public/theme-boot.js sets the theme before the first paint, the app follows the OS under
// System, and with storage blocked CSS alone follows the OS.

const darkBackground = "oklch(0.16 0.005 286)";

// vite preview has no API: answer boot's config call (spec 0016 FRS-2) so the app renders its own pages.
test.beforeEach(async ({ page }) => {
  await mockSignedOut(page);
  await page.route("**/v1/console/config", (route) =>
    route.fulfill({ json: { setupRequired: false } }),
  );
});

test("0015 AC-4: a saved dark theme applies before the app script runs", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.addInitScript(() => {
    localStorage.setItem("orvano:v1:theme", JSON.stringify("dark"));
  });
  // Block every module script, so only theme-boot.js runs.
  await page.route("**/assets/*.js", (route) => route.abort());

  await page.goto("./");

  const html = page.locator("html");
  await expect(html).toHaveAttribute("data-theme", "dark");
  expect(await html.evaluate((element) => element.style.colorScheme)).toBe("dark");
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(
    darkBackground,
  );
});

test("0015 AC-4: with storage blocked the console follows the OS theme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.addInitScript(() => {
    Object.defineProperty(window, "localStorage", {
      get() {
        throw new DOMException("Blocked", "SecurityError");
      },
    });
  });

  await page.goto("./");

  await expect(page.locator("main")).toBeVisible();
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(
    darkBackground,
  );
});

test("0015 AC-4: with the boot script failed, CSS alone follows the OS theme", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.route("**/theme-boot.js", (route) => route.abort());
  await page.route("**/assets/*.js", (route) => route.abort());

  await page.goto("./");

  await expect(page.locator("html")).not.toHaveAttribute("data-theme", /.*/);
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(
    darkBackground,
  );
});

test("0015 AC-6: with the boot script failed, more contrast still applies in the OS dark theme", async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: "dark", contrast: "more" });
  await page.route("**/theme-boot.js", (route) => route.abort());
  await page.route("**/assets/*.js", (route) => route.abort());

  await page.goto("./");

  await expect(page.locator("html")).not.toHaveAttribute("data-theme", /.*/);
  const tokens = await page.evaluate(() => {
    const style = getComputedStyle(document.documentElement);
    const token = (name: string) => style.getPropertyValue(`--color-${name}`).trim();
    return {
      border: token("border"),
      borderStrong: token("border-strong"),
      fgMuted: token("fg-muted"),
      fg: token("fg"),
    };
  });
  expect(await page.evaluate(() => getComputedStyle(document.body).backgroundColor)).toBe(
    darkBackground,
  );
  expect(tokens.border).toBe(tokens.borderStrong);
  expect(tokens.fgMuted).toBe(tokens.fg);
});

test("0015 AC-4: under System an OS theme change applies at once, with no reload", async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.goto("./");
  await expect(page.locator("main")).toBeVisible();
  const html = page.locator("html");
  await expect(html).toHaveAttribute("data-theme", "light");

  await page.emulateMedia({ colorScheme: "dark" });

  await expect(html).toHaveAttribute("data-theme", "dark");
});

test("0015 AC-6: in forced colors mode the keyboard focus ring stays visible", async ({ page }) => {
  await page.emulateMedia({ forcedColors: "active" });
  await page.goto("./");
  await expect(page.locator("main")).toBeVisible();

  await page.keyboard.press("Tab");

  const focused = page.locator(":focus");
  await expect(focused).toBeVisible();
  const outline = await focused.evaluate((element) => {
    const style = getComputedStyle(element);
    return { style: style.outlineStyle, width: style.outlineWidth };
  });
  expect(outline.style).not.toBe("none");
  expect(outline.width).not.toBe("0px");
});
