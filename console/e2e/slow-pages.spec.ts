import { expect, test, type Page, type Route } from "@playwright/test";
import { deferred } from "../src/test/deferred";
import { mockConsoleApi, ORG_ID, PROJECT_ID } from "./mocks";

// Spec 0017 AC-21, spec 0001 UX-21, spec 0003 RTE-3, RTE-4: a slow page shows a skeleton after 300 ms, inside the shell.

const emptyPage = { items: [], nextCursor: null, total: null };

/** Holds every matching call until the returned function is called. */
async function hold(page: Page, url: string, answer: (route: Route) => Promise<void>) {
  const held = deferred<undefined>();
  await page.route(url, async (route) => {
    await held.promise;
    await answer(route);
  });
  return () => {
    held.resolve(undefined);
  };
}

const skeleton = (page: Page) => page.getByRole("main").locator('[aria-busy="true"]');

test("0017 AC-21: a direct load of a slow list shows the shell and a skeleton, then the page", async ({
  page,
}) => {
  await mockConsoleApi(page);
  const release = await hold(page, `**/v1/console/organizations/${ORG_ID}/projects*`, (route) =>
    route.fulfill({ json: emptyPage }),
  );

  await page.goto(ORG_ID, { waitUntil: "commit" });

  await expect(skeleton(page)).toBeVisible();
  await expect(page.getByRole("button", { name: "Account" })).toBeVisible();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeHidden();

  release();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
  await expect(skeleton(page)).toBeHidden();
});

// RTE-9: `$orgId` waits for the organization in `beforeLoad`, which keeps every route pending, so the shell can't show
// yet. The root's pending component stands in for it.
test("0017 AC-21: a direct load with a slow organization shows a frame and a skeleton", async ({
  page,
}) => {
  await mockConsoleApi(page);
  const release = await hold(page, `**/v1/console/organizations/${ORG_ID}`, (route) =>
    route.fulfill({
      json: {
        id: ORG_ID,
        name: "Acme",
        allowedActions: [],
        version: "AAAAAAAAAAE",
        createdAt: "2030-01-01T00:00:00Z",
        updatedAt: "2030-01-01T00:00:00Z",
      },
    }),
  );

  await page.goto(`${ORG_ID}/settings`, { waitUntil: "commit" });

  await expect(skeleton(page)).toBeVisible();

  release();
  await expect(
    page.getByRole("heading", { level: 1, name: "Organization settings" }),
  ).toBeVisible();
});

test("0017 AC-21: a client navigation to a slow tab replaces the old tab with a skeleton", async ({
  page,
}) => {
  await mockConsoleApi(page);
  await page.goto(`${ORG_ID}/${PROJECT_ID}/settings/general`);
  await expect(page.getByRole("textbox", { name: "Name" })).toBeVisible();
  const release = await hold(page, `**/v1/console/projects/${PROJECT_ID}/keys*`, (route) =>
    route.fulfill({ json: emptyPage }),
  );

  await page.getByRole("link", { name: "API keys" }).click();

  await expect(skeleton(page)).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Name" })).toBeHidden();
  await expect(page.getByRole("navigation", { name: "Breadcrumb" })).toContainText("API keys");
  await expect(page.getByRole("heading", { level: 1, name: "Project settings" })).toBeVisible();

  release();
  await expect(skeleton(page)).toBeHidden();
});
