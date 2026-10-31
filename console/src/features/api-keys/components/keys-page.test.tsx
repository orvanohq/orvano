import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { http, shellHandlers, testOrgId, testProjectId } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

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
    await expect.element(screen.getByRole("checkbox", { name: "storage.read" })).not.toBeChecked();
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
    await screen.getByText("storage.read").click();
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(sent).toEqual([{ type: "secret", name: "Worker", scopes: ["storage.read"] }]);
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

  it("0017 AKY-1: a publishable key leaves its scopes to the server's default", async () => {
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
    await screen.getByRole("button", { name: "Create key" }).click();

    await expect.element(screen.getByRole("textbox", { name: "API key" })).toHaveValue(KEY);
    expect(sent).toEqual([{ type: "publishable", name: "Worker" }]);
  });
});
