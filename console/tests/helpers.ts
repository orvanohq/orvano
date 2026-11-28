import type { Page } from "@playwright/test";

import { instanceNav, serviceNavItems } from "../src/shell/nav";

export { instanceNav, serviceNavItems };

export const projectId = "prj_01JF8K2M4Q";

/** A page path relative to the base URL (`/console/`), so `page.goto(path("/members/"))` works. */
export const path = (route: string) => `.${route}`;

export const projectPath = (route: string, id = projectId) =>
  `${path(route)}?project=${id}`;

/** Waits until React has hydrated the page, so client behaviour (links, menus) is live. */
export async function hydrated(page: Page) {
  await page.waitForFunction(() =>
    document.documentElement.hasAttribute("data-theme"),
  );
  await page.waitForLoadState("networkidle");
}
