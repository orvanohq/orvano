import { expect, test } from "@playwright/test";
import { compose } from "./stack";

const EMAIL = "owner@example.com";
const PASSWORD = "correct horse battery staple";

// Spec 0017 AC-27, AC-1, AC-5, AC-12 to AC-15: the real console, server, worker, and PostgreSQL, from first run setup to a
// key that answers over HTTP, then its revoke, and the project's delete and teardown.
test("0017 AC-27: first run setup, sign in again, a project, a scoped key shown once, a live keys.current, its revoke, and a project delete", async ({
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
  await page.getByText("Read storage", { exact: true }).click();
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

  // Revoked, it stops on the next request.
  await page.getByRole("button", { name: "Actions for Worker" }).click();
  await page.getByRole("menuitem", { name: "Revoke" }).click();
  await page.getByRole("button", { name: "Revoke key" }).click();
  await expect(page.getByRole("row", { name: /Worker/ }).getByText("Revoked")).toBeVisible();
  const revoked = await request.get(`/v1/projects/${projectId}/keys/current`, {
    headers: { "Orvano-Key": key },
  });
  expect(revoked.status()).toBe(401);

  // A project delete asks for its name, and the list watches it go.
  await page.getByRole("link", { name: "General" }).click();
  await page.getByRole("button", { name: "Delete project" }).click();
  const confirm = page.getByRole("alertdialog", { name: "Delete Shop?" });
  await expect(confirm.getByText("1 API key")).toBeVisible();
  await confirm.getByRole("textbox").fill("Shop");
  await confirm.getByRole("button", { name: "Delete project" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Create your first project" })).toBeVisible({
    timeout: 30_000,
  });
});
