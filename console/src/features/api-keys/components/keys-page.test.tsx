import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import {
  http,
  problem,
  shellHandlers,
  testOrgId,
  testProject,
  testProjectId,
} from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";
import type { ApiKey } from "../api/queries";

const KEY = "orv_sk_dGhpcyBpcyBhIHRlc3Qga2V5IG9mIDMyIGJ5dGVzIGxvbmcx";
const path = `/${testOrgId}/${testProjectId}/settings/keys`;
const created = {
  id: "01970000-0000-7000-8000-00000000000c",
  projectId: testProjectId,
  type: "secret",
  name: "Worker",
  prefix: KEY.slice(0, 15),
  last4: KEY.slice(-4),
  scopes: ["storage.read"],
  status: "active",
  lastUsedAt: null,
  expiresAt: null,
  revokedAt: null,
  createdAt: "2030-01-01T00:00:00Z",
  allowedActions: ["keys.read", "keys.write"],
  key: KEY,
} as const;

async function openForm(screen: Awaited<ReturnType<typeof renderWithProviders>>) {
  await screen.getByRole("button", { name: "Create API key" }).click();
  await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Worker");
}

// Spec 0017 AKY-4, AC-13: a key is shown once, and only the form's own state ever holds it.
describe("the keys page", () => {
  it("0017 AKY-4: a secret key's scopes start unticked and need one", async () => {
    worker.use(...shellHandlers);
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await expect.element(screen.getByRole("checkbox", { name: "Read storage" })).not.toBeChecked();
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByText("Choose at least one scope.").first()).toBeVisible();
  });

  it("0017 AC-13: shows the new key once, then only its mask, and nothing caches it", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys", async ({ request }) => {
        sent.push(await request.clone().json());
        return Response.json(created, { status: 201 });
      }),
    );
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await screen.getByText("Read storage").click();
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(sent).toEqual([{ type: "secret", name: "Worker", scopes: ["storage.read"] }]);
    await expect.element(screen.getByRole("tab", { name: "Node.js" })).toBeVisible(); // UX-37
    await screen.getByRole("button", { name: "I've copied it" }).click();
    await expect
      .element(screen.getByText(`${created.prefix}…${created.last4}`).first())
      .toBeVisible();
    expect(screen.container.textContent).not.toContain(KEY);
    await expect
      .poll(() =>
        JSON.stringify(
          screen.queryClient
            .getQueryCache()
            .getAll()
            .map((query) => query.state.data),
        ),
      )
      .not.toContain(KEY);
  });

  it("0017 AKY-4: a publishable key starts with every client scope, and Sign up goes with Sign in", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys", async ({ request }) => {
        sent.push(await request.clone().json());
        return Response.json({ ...created, type: "publishable" }, { status: 201 });
      }),
    );
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await screen.getByText("Publishable").click();
    await expect.element(screen.getByRole("checkbox", { name: "Sign up" })).toBeChecked();
    await screen.getByText("Sign in", { exact: true }).click();
    await expect.element(screen.getByRole("checkbox", { name: "Sign up" })).not.toBeChecked();
    await expect.element(screen.getByRole("checkbox", { name: "Sign up" })).toBeDisabled();
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(sent).toEqual([
      {
        type: "publishable",
        name: "Worker",
        scopes: ["client.databases", "client.functions", "client.realtime", "client.storage"],
      },
    ]);
  });

  it("0017 AKY-4: select all ticks a whole service, and an expiry sends its days", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys", async ({ request }) => {
        sent.push(await request.clone().json());
        return Response.json(created, { status: 201 });
      }),
    );
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await screen.getByText("All of Storage").click();
    await expect.element(screen.getByRole("checkbox", { name: "Write storage" })).toBeChecked();
    await screen.getByRole("button", { name: /Expires/ }).click();
    await screen.getByRole("option", { name: "In 90 days" }).click();
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(sent).toEqual([
      {
        type: "secret",
        name: "Worker",
        scopes: ["storage.read", "storage.write"],
        expiresInDays: 90,
      },
    ]);
  });

  it("0017 SES-8, AC-8: asks for the password on reauth_required, then retries once", async () => {
    let creates = 0;
    const passwords: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys", () => {
        creates += 1;
        return creates === 1
          ? problem(403, "reauth_required")
          : Response.json(created, { status: 201 });
      }),
      http.post("/v1/console/session/reauth", async ({ request }) => {
        const { password } = await request.clone().json();
        passwords.push(password);
        if (password === "right password") return new Response(null, { status: 204 });
        return Response.json(
          {
            type: "about:blank",
            title: "Problem",
            status: 400,
            detail: "A field in the request is not valid.",
            code: "validation_failed",
            errors: [
              { field: "password", location: "body", code: "invalid_password", message: "Wrong." },
            ],
          },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        );
      }),
    );
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await screen.getByText("Read storage").click();
    await screen.getByRole("button", { name: "Create key" }).click();

    const dialog = screen.getByRole("dialog", { name: "Confirm your password" });
    await expect.element(dialog).toBeVisible();
    await userEvent.fill(dialog.getByLabelText("Password"), "wrong password");
    await dialog.getByRole("button", { name: "Confirm" }).click();
    await expect.element(dialog.getByText("That password is wrong.").first()).toBeVisible();
    expect(creates).toBe(1);

    await userEvent.fill(dialog.getByLabelText("Password"), "right password");
    await dialog.getByRole("button", { name: "Confirm" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(creates).toBe(2);
    expect(passwords).toEqual(["wrong password", "right password"]);
  });

  it("0017 SES-8: dismissing the prompt keeps the form and says why nothing was made", async () => {
    let creates = 0;
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys", () => {
        creates += 1;
        return problem(403, "reauth_required");
      }),
    );
    const screen = await renderWithProviders(path);

    await openForm(screen);
    await screen.getByText("Read storage").click();
    await screen.getByRole("button", { name: "Create key" }).click();
    const dialog = screen.getByRole("dialog", { name: "Confirm your password" });
    await dialog.getByRole("button", { name: "Cancel" }).click();

    await expect
      .element(
        screen.getByText("The key wasn't created, because your password wasn't confirmed.").first(),
      )
      .toBeVisible();
    await expect.element(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Worker");
    expect(creates).toBe(1);
  });
});

const key = {
  id: created.id,
  projectId: testProjectId,
  type: "secret",
  name: "Worker",
  prefix: created.prefix,
  last4: created.last4,
  scopes: ["storage.read"],
  status: "active",
  lastUsedAt: null,
  expiresAt: "2030-06-01T00:00:00Z",
  revokedAt: null,
  createdAt: "2030-01-01T00:00:00Z",
  allowedActions: ["keys.read", "keys.write"],
} satisfies ApiKey;

const keyList = (items: ApiKey[]) =>
  http.get("/v1/console/projects/{projectId}/keys", ({ response }) =>
    response(200).json({ items, nextCursor: null, total: null }),
  );

async function openAction(screen: Awaited<ReturnType<typeof renderWithProviders>>, name: string) {
  await screen.getByRole("button", { name: "Actions for Worker" }).click();
  await screen.getByRole("menuitem", { name }).click();
}

// Spec 0017 AKY-5 to AKY-8, AC-14.
describe("a key's actions", () => {
  it("0017 AKY-6, UX-23: revoke is a Tier 2 confirm that starts on Cancel", async () => {
    const revoked: string[] = [];
    worker.use(
      keyList([key]),
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys/{keyId}/revoke", ({ params, response }) => {
        revoked.push(params.keyId);
        return response(200).json({ ...key, status: "revoked" });
      }),
    );
    const screen = await renderWithProviders(path);

    await openAction(screen, "Revoke");
    const dialog = screen.getByRole("alertdialog", { name: "Revoke Worker?" });
    await expect.element(dialog.getByRole("button", { name: "Cancel" })).toHaveFocus();
    await dialog.getByRole("button", { name: "Revoke key" }).click();

    await expect.element(dialog).not.toBeInTheDocument();
    await expect.element(screen.getByText("Revoked", { exact: true })).toBeVisible();
    expect(revoked).toEqual([key.id]);
  });

  it("0017 AKY-7, AC-14: regenerate says the old key stops, then shows the new one once", async () => {
    worker.use(
      keyList([key]),
      ...shellHandlers,
      http.post("/v1/console/projects/{projectId}/keys/{keyId}/regenerate", () =>
        Response.json(created, { status: 201 }),
      ),
    );
    const screen = await renderWithProviders(path);

    await openAction(screen, "Regenerate");
    const dialog = screen.getByRole("alertdialog", { name: "Regenerate Worker?" });
    await expect
      .element(dialog.getByText("The old key stops at once", { exact: false }))
      .toBeVisible();
    await dialog.getByRole("button", { name: "Regenerate key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    await screen.getByRole("button", { name: "I've copied it" }).click();
    await screen.getByRole("button", { name: "Done" }).click();
    expect(screen.container.textContent).not.toContain(KEY);
    expect(
      JSON.stringify(
        screen.queryClient
          .getMutationCache()
          .getAll()
          .map((m) => m.state.data),
      ),
    ).not.toContain(KEY);
  });

  it("0017 AKY-8: rename patches only the name, and the row shows it", async () => {
    const sent: unknown[] = [];
    worker.use(
      keyList([key]),
      ...shellHandlers,
      http.patch("/v1/console/projects/{projectId}/keys/{keyId}", async ({ request, response }) => {
        sent.push(await request.clone().json());
        return response(200).json({ ...key, name: "Renamed" });
      }),
    );
    const screen = await renderWithProviders(path);

    await openAction(screen, "Rename");
    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Renamed");
    await screen.getByRole("button", { name: "Rename" }).click();

    await expect.element(screen.getByRole("rowheader", { name: "Renamed" })).toBeVisible();
    expect(sent).toEqual([{ name: "Renamed" }]);
  });

  it("0017 AKY-5, UX-30: a revoked key says why it can't be revoked or regenerated", async () => {
    worker.use(keyList([{ ...key, status: "revoked" }]), ...shellHandlers);
    const screen = await renderWithProviders(path);

    await screen.getByRole("button", { name: "Actions for Worker" }).click();

    await expect.element(screen.getByText("It's already revoked.")).toBeVisible();
    await expect.element(screen.getByText("Only an active key can be regenerated.")).toBeVisible();
    await expect
      .element(screen.getByRole("menuitem", { name: /^Revoke/ }))
      .toHaveAttribute("aria-disabled", "true");
  });

  it("0003 RTE-9, UX-30: an action the operator can't take stays visible and says why", async () => {
    worker.use(
      http.get("/v1/console/projects/{projectId}", ({ response }) =>
        response(200).json({ ...testProject, allowedActions: ["keys.read"] }),
      ),
      keyList([{ ...key, allowedActions: ["keys.read"] }]),
      ...shellHandlers,
    );
    const screen = await renderWithProviders(path);

    await expect
      .element(screen.getByRole("button", { name: "Create API key" }))
      .toHaveAttribute("aria-disabled", "true");
    await screen.getByRole("button", { name: "Actions for Worker" }).click();
    await expect
      .element(screen.getByRole("menuitem", { name: /^Rename/ }))
      .toHaveAttribute("aria-disabled", "true");
  });
});
