import { expect, test, type Page } from "@playwright/test";
import { compose } from "./stack";

const EMAIL = "owner@example.com";
const PASSWORD = "correct horse battery staple";
const CHANGED_PASSWORD = "a brand new passphrase";
const RESET_PASSWORD = "another fine passphrase";

/** Runs SQL as the database's owner. Only ever test values. */
function sql(statement: string) {
  compose("exec", "-T", "postgres", "psql", "-U", "postgres", "-d", "orvano", "-c", statement);
}

async function signIn(page: Page, password: string) {
  await page.getByRole("textbox", { name: "Email" }).fill(EMAIL);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

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

// Spec 0017 AC-6, AC-18, AC-19, spec 0003 QRY-8, QRY-9: what only the real cookie, two real tabs, and the host command can
// show. It carries on from the install the test above made.
test("0017 AC-6, AC-18, AC-19: a password change, sign out in every tab, a reset link, and a session that ends mid edit", async ({
  page,
  context,
}) => {
  await page.goto("sign-in");
  await signIn(page, PASSWORD);
  await expect(page.getByRole("button", { name: "Account" })).toBeVisible();
  const otherTab = await context.newPage();
  await otherTab.goto("account");
  await expect(otherTab.getByRole("heading", { level: 1, name: "Account" })).toBeVisible();

  // The password, through the account menu. The cookie and CSRF token change, and the other tab keeps working.
  await page.getByRole("button", { name: "Account" }).click();
  await page.getByRole("menuitem", { name: "Account" }).click();
  await page.getByLabel("Current password", { exact: true }).fill(PASSWORD);
  await page.getByLabel("New password", { exact: true }).fill(CHANGED_PASSWORD);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Password changed")).toBeVisible();
  await expect(page.getByLabel("Current password", { exact: true })).toHaveValue("");

  await otherTab.getByRole("textbox", { name: "Name" }).fill("Grace Owner");
  await otherTab
    .getByRole("region", { name: "Profile" })
    .getByRole("button", { name: "Update" })
    .click();
  await expect(
    otherTab.getByRole("region", { name: "Profile" }).getByRole("button", { name: "Update" }),
  ).toHaveAttribute("aria-disabled", "true");
  await page.reload();
  await expect(page.getByRole("textbox", { name: "Name" })).toHaveValue("Grace Owner");

  // Sign out ends the session in every tab.
  await page
    .getByRole("region", { name: "Sign out" })
    .getByRole("button", { name: "Sign out" })
    .click();
  await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
  await expect(otherTab.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
  await otherTab.close();

  // A reset link, held only in this variable. The address drops the token.
  const link = compose(
    "exec",
    "-T",
    "orvano",
    "/app/orvano",
    "admin",
    "reset-operator",
    "--email",
    EMAIL,
  ).trim();
  expect(link).toContain("/console/reset#token=orv_reset_");
  await page.goto(link);
  await expect(page.getByRole("heading", { level: 1, name: "Reset your password" })).toBeVisible();
  expect(page.url()).not.toContain("token");
  await page.getByLabel("New password", { exact: true }).fill(RESET_PASSWORD);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();

  // The link works once.
  await page.goto(link);
  await page.getByLabel("New password", { exact: true }).fill(CHANGED_PASSWORD);
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("This link isn't valid any more.")).toBeVisible();

  await page.goto("sign-in");
  await signIn(page, RESET_PASSWORD);
  await expect(page.getByRole("button", { name: "Account" })).toBeVisible();

  // The session ends mid edit: the form stays, the dialog opens over it, and the save goes through after sign in.
  await page.goto("account");
  await page.getByRole("textbox", { name: "Name" }).fill("Ada Lovelace");
  sql("UPDATE platform.operator_session SET revoked_at = now()");
  await page
    .getByRole("region", { name: "Profile" })
    .getByRole("button", { name: "Update" })
    .click();
  const dialog = page.getByRole("dialog", { name: "Your session ended" });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Password", { exact: true }).fill(RESET_PASSWORD);
  await dialog.getByRole("button", { name: "Sign in" }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole("textbox", { name: "Name" })).toHaveValue("Ada Lovelace");
  await expect(
    page.getByRole("region", { name: "Profile" }).getByRole("button", { name: "Update" }),
  ).toHaveAttribute("aria-disabled", "true");
  await page.reload();
  await expect(page.getByRole("textbox", { name: "Name" })).toHaveValue("Ada Lovelace");
});
