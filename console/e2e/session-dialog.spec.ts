import { expect, test, type Page } from "@playwright/test";
import { mockConsoleApi, ORG_ID, SESSION, unauthorized } from "./mocks";

// Spec 0017 SES-9, spec 0003 QRY-8: someone else signing in through the session dialog. The page holds the first
// operator's data and the cookie is already the second's, so the reload never asks. Only a real document load fires the
// browser's leave prompt, which is why this runs here.

const someoneElse = {
  ...SESSION,
  operator: {
    ...SESSION.operator,
    id: "01970000-0000-7000-8000-000000000002",
    name: "Grace",
    email: "grace@example.com",
  },
};

/** Signs in through the open dialog as `someoneElse`, and answers what the prompts recorded. */
async function signInAsSomeoneElse(page: Page) {
  const prompts: string[] = [];
  page.on("dialog", async (dialog) => {
    prompts.push(dialog.type());
    await dialog.dismiss();
  });
  const dialog = page.getByRole("dialog", { name: "Your session ended" });
  await expect(dialog).toBeVisible();
  await page.route("**/v1/console/session", (route) =>
    route.fulfill({
      status: route.request().method() === "POST" ? 201 : 200,
      json: someoneElse,
    }),
  );
  await page.unroute(`**/v1/console/organizations/${ORG_ID}/projects*`);
  await page.route(`**/v1/console/organizations/${ORG_ID}/projects*`, (route) =>
    route.fulfill({ json: { items: [], nextCursor: null, total: null } }),
  );
  await dialog.getByRole("textbox", { name: "Email" }).fill(someoneElse.operator.email);
  await dialog.getByLabel("Password", { exact: true }).fill("correct horse");
  const reload = page.waitForRequest((request) => request.isNavigationRequest());
  await dialog.getByRole("button", { name: "Sign in" }).click();
  await reload;
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
  await expect(page.getByRole("dialog", { name: "Your session ended" })).toBeHidden();
  return prompts;
}

test("0003 QRY-8: a different operator reloads with no leave prompt from a clean page", async ({
  page,
}) => {
  await mockConsoleApi(page);
  await page.goto(`${ORG_ID}/settings`);
  await expect(
    page.getByRole("heading", { level: 1, name: "Organization settings" }),
  ).toBeVisible();

  await page.route(`**/v1/console/organizations/${ORG_ID}/projects*`, (route) =>
    unauthorized(route, "session_expired"),
  );
  await page.getByRole("navigation", { name: "Organization" }).getByRole("link").first().click();

  expect(await signInAsSomeoneElse(page)).toEqual([]);
});

test("0003 QRY-8: a different operator reloads with no leave prompt past a changed form too", async ({
  page,
}) => {
  await mockConsoleApi(page);
  await page.route("**/v1/console/account", (route) => unauthorized(route, "session_expired"));
  await page.goto("account");
  await page.getByRole("textbox", { name: "Name" }).fill("Typed, not saved");
  await page
    .getByRole("region", { name: "Profile" })
    .getByRole("button", { name: "Update" })
    .click();

  expect(await signInAsSomeoneElse(page)).toEqual([]);
});
