import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

import { hydrated, path, projectPath } from "./helpers";

const pages = [
  ["projects", path("/")],
  ["a project stub", projectPath("/project/database/")],
  ["the kit", path("/kit/")],
] as const;

for (const colorScheme of ["light", "dark"] as const) {
  for (const [name, url] of pages) {
    test(`axe finds no WCAG A or AA violations on ${name} in ${colorScheme}`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme });
      await page.goto(url);
      await hydrated(page);
      await expect(page.locator("html")).toHaveAttribute(
        "data-theme",
        colorScheme,
      );
      const results = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
        .analyze();
      expect(
        results.violations.map((v) => ({
          id: v.id,
          nodes: v.nodes.map((n) => n.target),
        })),
      ).toEqual([]);
    });
  }
}

for (const colorScheme of ["light", "dark"] as const) {
  test(`axe finds no WCAG A or AA violations with the kit side sheet open in ${colorScheme}`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme });
    await page.goto(path("/kit/"));
    await hydrated(page);
    await page.getByRole("button", { name: "dlv_01JF8K2M4Q02" }).click();
    await expect(page.getByRole("dialog")).toHaveCSS("width", "480px");
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(
      results.violations.map((v) => ({
        id: v.id,
        nodes: v.nodes.map((n) => n.target),
      })),
    ).toEqual([]);
  });
}

test("the skip link is the first tab stop and moves focus to main", async ({
  page,
}) => {
  await page.goto(path("/members/"));
  await hydrated(page);
  await page.keyboard.press("Tab");
  const skip = page.getByRole("link", { name: "Skip to main content" });
  await expect(skip).toBeFocused();
  await expect(skip).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(page.locator("main#main")).toBeFocused();
});

test("with reduced motion, sheets, tooltips and hovers have no transition", async ({
  page,
}) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(path("/kit/"));
  await hydrated(page);
  const button = page.getByRole("button", { name: "Theme" });
  expect(
    await button.evaluate((node) => getComputedStyle(node).transitionDuration),
  ).toBe("0s");
  await page.getByRole("button", { name: "dlv_01JF8K2M4Q01" }).click();
  const sheet = page.getByRole("dialog");
  await expect(sheet).toBeVisible();
  expect(
    await sheet.evaluate((node) => getComputedStyle(node).animationDuration),
  ).toBe("0s");
});
