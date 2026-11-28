import { expect, test, type Page } from "@playwright/test";

import { hydrated, path } from "./helpers";

declare global {
  interface Window {
    __themeAtFirstFrame?: string | null;
  }
}

const theme = (page: Page) => page.locator("html").getAttribute("data-theme");

async function choose(page: Page, choice: "System" | "Light" | "Dark") {
  await page.getByRole("button", { name: "Theme" }).click();
  await page.getByRole("menuitemradio", { name: choice }).click();
}

test.beforeEach(async ({ page }) => {
  // Records data-theme in the first animation frame, which runs before the first paint.
  await page.addInitScript(() => {
    requestAnimationFrame(() => {
      window.__themeAtFirstFrame =
        document.documentElement.getAttribute("data-theme");
    });
  });
});

test("System is the default and follows the OS, including a change while the page is open", async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto(path("/"));
  await hydrated(page);
  expect(await theme(page)).toBe("dark");
  await page.getByRole("button", { name: "Theme" }).click();
  await expect(
    page.getByRole("menuitemradio", { name: "System" }),
  ).toHaveAttribute("aria-checked", "true");
  await page.keyboard.press("Escape");

  await page.emulateMedia({ colorScheme: "light" });
  await expect.poll(() => theme(page)).toBe("light");
});

for (const [os, choice, expected] of [
  ["dark", "Light", "light"],
  ["light", "Dark", "dark"],
] as const) {
  test(`${choice} overrides an OS in ${os} mode and survives a reload from the first paint`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: os });
    await page.goto(path("/"));
    await hydrated(page);
    await choose(page, choice);
    await expect.poll(() => theme(page)).toBe(expected);

    await page.reload();
    await page.waitForFunction(() => window.__themeAtFirstFrame !== undefined);
    expect(await page.evaluate(() => window.__themeAtFirstFrame)).toBe(
      expected,
    );
    expect(await theme(page)).toBe(expected);
    expect(
      await page.evaluate(() => localStorage.getItem("orvano-theme")),
    ).toBe(expected);
  });
}

test("the menu offers System, Light and Dark and is reachable by keyboard", async ({
  page,
}) => {
  await page.goto(path("/"));
  await hydrated(page);
  await page.getByRole("button", { name: "Theme" }).focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("menuitemradio")).toHaveText([
    "System",
    "Light",
    "Dark",
  ]);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Theme" })).toBeFocused();
});
