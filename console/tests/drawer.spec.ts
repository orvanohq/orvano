import { expect, test } from "@playwright/test";

import { hydrated, projectId, projectPath } from "./helpers";

test.use({ viewport: { width: 375, height: 812 } });

test("below 768px the rail is a drawer that keeps and returns focus", async ({
  page,
}) => {
  await page.goto(projectPath("/project/auth/"));
  await hydrated(page);
  await expect(page.getByRole("navigation", { name: "Project" })).toBeHidden();

  const menu = page.getByRole("button", { name: "Open navigation" });
  await expect(menu).toHaveAttribute("aria-expanded", "false");
  await menu.click();

  const drawer = page.getByRole("dialog", { name: "Navigation" });
  await expect(drawer).toBeVisible();
  // The modal hides everything else from assistive tech, the menu button included.
  await expect(
    page.getByRole("button", { name: "Open navigation", includeHidden: true }),
  ).toHaveAttribute("aria-expanded", "true");
  await expect(drawer.getByRole("link", { name: "Database" })).toBeVisible();

  // Focus stays inside while tabbing past every item.
  for (let i = 0; i < 20; i++) {
    await page.keyboard.press("Tab");
    expect(
      await drawer.evaluate((node) => node.contains(document.activeElement)),
    ).toBe(true);
  }

  await page.keyboard.press("Escape");
  await expect(drawer).toBeHidden();
  await expect(menu).toBeFocused();
});

test("choosing an item closes the drawer and keeps the project", async ({
  page,
}) => {
  await page.goto(projectPath("/project/auth/"));
  await hydrated(page);
  await page.getByRole("button", { name: "Open navigation" }).click();
  await page
    .getByRole("dialog", { name: "Navigation" })
    .getByRole("link", { name: "Webhooks" })
    .click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Webhooks");
  await expect(page).toHaveURL(
    new RegExp(`/console/project/webhooks/\\?project=${projectId}$`),
  );
});
