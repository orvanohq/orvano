import { expect, test, type Page } from "@playwright/test";
import { mockConsoleApi, ORG_ID, SESSION, unauthorized } from "./mocks";

// Spec 0017 AC-6, spec 0003 URL-5: where a correct sign in lands, on the built console, where a redirect off the origin
// is a real navigation. `evil.example` never leaves this machine: the route below answers for it.

async function signIn(page: Page, redirect: string) {
  await mockConsoleApi(page);
  await page.route("**/v1/console/session", (route) =>
    route.request().method() === "POST"
      ? route.fulfill({ status: 201, json: SESSION })
      : unauthorized(route),
  );
  await page.route(/\/\/evil\.example\//, (route) =>
    route.fulfill({ contentType: "text/html", body: "<title>attacker page</title>" }),
  );
  await page.goto(`sign-in?redirect=${encodeURIComponent(redirect)}`);
  await page.getByRole("textbox", { name: "Email" }).fill("ada@example.com");
  await page.getByLabel("Password", { exact: true }).fill("correct horse");
  await page.getByRole("button", { name: "Sign in" }).click();
}

for (const redirect of [
  "https://evil.example/landing",
  "http://evil.example/landing",
  " https://evil.example/landing",
  "ht\ttps://evil.example/landing",
  "//evil.example/landing",
  "\\\\evil.example/landing",
  "/\t/evil.example/landing",
  "/\n/evil.example/landing",
  "javascript:alert(1)",
  "data:text/html,x",
]) {
  test(`0017 AC-6: a sign in with redirect ${JSON.stringify(redirect)} lands on the console's home`, async ({
    page,
  }) => {
    await signIn(page, redirect);

    await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
    expect(new URL(page.url()).hostname).toBe("localhost");
    expect(new URL(page.url()).pathname).toBe(`/console/${ORG_ID}`);
  });
}

test("0017 AC-6: a path on this origin still comes back, with its search", async ({ page }) => {
  await signIn(page, `/${ORG_ID}/settings?size=25`);

  await expect(
    page.getByRole("heading", { level: 1, name: "Organization settings" }),
  ).toBeVisible();
  expect(page.url()).toContain(`/console/${ORG_ID}/settings`);
});
