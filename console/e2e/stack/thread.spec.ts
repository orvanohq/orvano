import { expect, test } from "@playwright/test";
import { compose } from "./stack";

const EMAIL = "owner@example.com";
const PASSWORD = "correct horse battery staple";

// Spec 0017 AC-27, AC-1, AC-5, AC-13, AC-15: the real console, server, and PostgreSQL, from first run setup to a key that
// answers over HTTP. Revoking it joins this run with build plan step 15.
test("0017 AC-27: first run setup, sign in again, a project, a scoped key shown once, and a live keys.current", async ({
  page,
  request,
}) => {
  const link = compose("exec", "-T", "orvano", "/app/orvano", "admin", "setup-token").trim();
  expect(link).toContain("/console/setup#token=");

  // The owner form.
  await page.goto(link);
  await expect(page.getByRole("heading", { level: 2, name: "Setup token accepted" })).toBeVisible();
  await page.getByRole("textbox", { name: "Your name" }).fill("Ada Owner");
  await page.getByRole("textbox", { name: "Email" }).fill(EMAIL);
  await page.getByRole("textbox", { name: "Organization name" }).fill("Acme");
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Create owner account" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
  expect(page.url()).not.toContain("token");

  // A reload keeps the session, so the cookie took.
  await page.reload();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();

  // Sign out, then sign in again.
  await page.getByRole("button", { name: "Account" }).click();
  await page.getByRole("menuitem", { name: "Sign out" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
  await page.getByRole("textbox", { name: "Email" }).fill(EMAIL);
  await page.getByLabel("Password", { exact: true }).fill(PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();

  // A project.
  await page.getByRole("button", { name: "New project" }).click();
  await page.getByRole("textbox", { name: "Name" }).fill("Shop");
  await page.getByRole("button", { name: "Create project" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Shop" })).toBeVisible();
  const projectId = await page.getByRole("textbox", { name: "Project ID" }).inputValue();

  // A scoped key, shown once.
  await page.getByRole("link", { name: "Create API key" }).click();
  await page.getByRole("button", { name: "Create API key" }).click();
  await page.getByRole("textbox", { name: "Name" }).fill("Worker");
  await page.getByText("storage.read", { exact: true }).click();
  await page.getByRole("button", { name: "Create key" }).click();
  const key = await page.getByRole("textbox", { name: "API key" }).inputValue();
  expect(key).toMatch(/^orv_sk_[\w-]{43}$/);
  await page.getByRole("button", { name: "I've copied it" }).click();
  await expect(page.getByRole("textbox", { name: "API key" })).toBeHidden();
  await page.getByRole("button", { name: "Done" }).click();
  await expect(page.getByRole("rowheader", { name: "Worker" })).toBeVisible();
  await expect(page.locator("body")).not.toContainText(key);

  // The key works.
  const response = await request.get(`/v1/projects/${projectId}/keys/current`, {
    headers: { "Orvano-Key": key },
  });
  expect(response.status()).toBe(200);
  expect(await response.json()).toEqual({ projectId, type: "secret", scopes: ["storage.read"] });

  // And nothing else does.
  const wrong = await request.get(`/v1/projects/${projectId}/keys/current`, {
    headers: { "Orvano-Key": `${key.slice(0, -4)}AAAA` },
  });
  expect(wrong.status()).toBe(401);
});
