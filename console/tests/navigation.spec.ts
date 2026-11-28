import { expect, test } from "@playwright/test";

import { hydrated, projectPath } from "./helpers";

declare global {
  interface Window {
    __orvanoMarker?: string;
  }
}

/**
 * Rail clicks are client side navigations: a marker on window survives, and no request fails. A missing or
 * misserved route data file would otherwise fall back silently to full page reloads.
 */
test("rail navigation stays in the app with no failed requests", async ({
  page,
}) => {
  const failures: string[] = [];
  page.on("response", (response) => {
    if (response.status() >= 400) {
      failures.push(`${response.status()} ${response.url()}`);
    }
  });

  await page.goto(projectPath("/project/overview/"));
  await hydrated(page);
  await page.evaluate(() => {
    window.__orvanoMarker = "still here";
  });

  const rail = page.getByRole("navigation", { name: "Project" });
  for (const label of [
    "Database",
    "Storage",
    "Functions",
    "Logs",
    "Settings",
  ]) {
    await rail.getByRole("link", { name: label }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(label);
    await expect(page).toHaveURL(/\?project=prj_01JF8K2M4Q$/);
  }
  await rail.getByRole("link", { name: "Projects" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Projects");

  expect(await page.evaluate(() => window.__orvanoMarker)).toBe("still here");
  expect(failures).toEqual([]);
});
