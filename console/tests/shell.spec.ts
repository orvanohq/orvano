import { expect, test } from "@playwright/test";

import {
  hydrated,
  instanceNav,
  path,
  projectId,
  projectPath,
  serviceNavItems,
} from "./helpers";

test.describe("instance rail", () => {
  test("lists the four instance pages and marks the current one", async ({
    page,
  }) => {
    await page.goto(path("/members/"));
    const rail = page.getByRole("navigation", { name: "Instance" });
    const links = rail.getByRole("link");
    await expect(links).toHaveCount(4);
    for (const [index, item] of instanceNav.entries()) {
      await expect(links.nth(index)).toHaveAccessibleName(item.label);
    }
    await expect(rail.getByRole("link", { name: "Members" })).toHaveAttribute(
      "aria-current",
      "page",
    );
    await expect(
      rail.getByRole("link", { name: "Projects" }),
    ).not.toHaveAttribute("aria-current");
    expect((await rail.boundingBox())?.width).toBe(56);
  });

  test("Projects is current only on the projects page", async ({ page }) => {
    await page.goto(path("/"));
    await expect(
      page
        .getByRole("navigation", { name: "Instance" })
        .getByRole("link", { name: "Projects" }),
    ).toHaveAttribute("aria-current", "page");
  });

  test("shows a tooltip with the label on focus", async ({ page }) => {
    await page.goto(path("/"));
    await hydrated(page);
    await page
      .getByRole("navigation", { name: "Instance" })
      .getByRole("link", { name: "Audit log" })
      .focus();
    await expect(page.getByRole("tooltip")).toHaveText("Audit log");
  });
});

test.describe("service rail", () => {
  test("lists the back link and the twelve services in order, carrying the project", async ({
    page,
  }) => {
    await page.goto(projectPath("/project/database/"));
    await hydrated(page);
    const rail = page.getByRole("navigation", { name: "Project" });
    const links = rail.getByRole("link");
    await expect(links).toHaveCount(13);
    await expect(links.first()).toHaveAccessibleName("Projects");
    await expect(links.first()).not.toHaveAttribute("aria-current");
    for (const [index, item] of serviceNavItems.entries()) {
      const link = links.nth(index + 1);
      await expect(link).toHaveAccessibleName(item.label);
      await expect(link).toHaveAttribute(
        "href",
        `/console${item.route}?project=${projectId}`,
      );
    }
    await expect(rail.getByRole("link", { name: "Database" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  test("the top bar shows the mark, the instance address and the project id", async ({
    page,
  }) => {
    await page.goto(projectPath("/project/storage/"));
    const header = page.getByRole("banner");
    await expect(header.getByRole("link", { name: "Orvano" })).toBeVisible();
    await expect(header.getByText("localhost:3100")).toBeVisible();
    await expect(header.getByText(projectId)).toBeVisible();
    expect((await header.boundingBox())?.height).toBe(48);
  });
});

test.describe("project guard", () => {
  for (const [name, query] of [
    ["missing", ""],
    ["invalid", "?project=bad%20id"],
  ] as const) {
    test(`a ${name} project id replaces the address with /console/`, async ({
      page,
    }) => {
      await page.goto(path("/members/"));
      await hydrated(page);
      await page.goto(`${path("/project/database/")}${query}`);
      await expect(page).toHaveURL(/\/console\/$/);
      await expect(page.getByRole("heading", { level: 1 })).toHaveText(
        "Projects",
      );
      // The project page left no history entry: going back lands on Members.
      await page.goBack();
      await expect(page).toHaveURL(/\/console\/members\/$/);
    });
  }

  test("never shows project content for an invalid id", async ({ page }) => {
    await page.goto(`${path("/project/database/")}?project=nope`);
    await expect(page).toHaveURL(/\/console\/$/);
    await expect(page.getByText("Database isn't built yet")).toHaveCount(0);
  });
});

test.describe("stub pages and titles", () => {
  for (const item of instanceNav) {
    test(`${item.title} (instance)`, async ({ page }) => {
      await page.goto(path(item.route));
      await expect(page).toHaveTitle(`${item.title} · Orvano`);
      await expect(page.getByRole("heading", { level: 1 })).toHaveText(
        item.title,
      );
      await expect(page.getByText(item.description)).toBeVisible();
      await expect(page.getByRole("heading", { level: 2 })).toHaveText(
        `${item.title} isn't built yet`,
      );
      await expect(
        page.getByText(`This page arrives in milestone ${item.milestone}.`),
      ).toBeVisible();
    });
  }

  for (const item of serviceNavItems) {
    test(`${item.title} (project)`, async ({ page }) => {
      await page.goto(projectPath(item.route));
      await expect(page).toHaveTitle(`${item.title} · Orvano`);
      await expect(page.getByRole("heading", { level: 1 })).toHaveText(
        item.title,
      );
      await expect(page.getByText(item.description)).toBeVisible();
      await expect(page.getByRole("heading", { level: 2 })).toHaveText(
        `${item.title} isn't built yet`,
      );
      await expect(
        page.getByText(`This page arrives in milestone ${item.milestone}.`),
      ).toBeVisible();
    });
  }

  test("the kit page has its title and no rail", async ({ page }) => {
    await page.goto(path("/kit/"));
    await expect(page).toHaveTitle("Base components · Orvano");
    await expect(page.getByRole("navigation")).toHaveCount(0);
  });
});

test.describe("not found", () => {
  test("an unknown page answers 404 with a link back to Projects and no rail", async ({
    page,
  }) => {
    const response = await page.goto(path("/no-such-page/"));
    expect(response?.status()).toBe(404);
    await expect(page).toHaveTitle("Page not found · Orvano");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(
      "Page not found",
    );
    await expect(page.getByRole("navigation")).toHaveCount(0);
    await hydrated(page);
    await page
      .getByRole("main")
      .getByRole("link", { name: "your projects" })
      .click();
    await expect(page).toHaveURL(/\/console\/$/);
  });

  test("an unknown project page has no rail either", async ({ page }) => {
    await page.goto(`${path("/project/nope/")}?project=${projectId}`);
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(
      "Page not found",
    );
    await hydrated(page);
    await expect(page.getByRole("navigation")).toHaveCount(0);
  });
});
