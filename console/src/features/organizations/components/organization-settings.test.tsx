import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { http, shellHandlers, testOrgId, testProject, testSession } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

const organization = {
  id: testOrgId,
  name: "Acme",
  allowedActions: ["org.read", "org.write", "projects.create"],
  version: "AAAAAAAAAAE",
  createdAt: "2030-01-01T00:00:00Z",
  updatedAt: "2030-01-01T00:00:00Z",
};
const second = { ...organization, id: "01970000-0000-7000-8000-00000000000f", name: "Globex" };

const projectList = (items: (typeof testProject)[]) =>
  http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) =>
    response(200).json({ items, nextCursor: null, total: null }),
  );

const sessionRead = http.get("/v1/console/session", ({ response }) =>
  response(200).json(testSession),
);

// Spec 0017 ORG-1 to ORG-3, CNS-3 to CNS-5, AC-9, AC-21.
describe("organizations", () => {
  it("0017 ORG-2, SHL-12: the switcher searches the paged list", async () => {
    const queries: (string | null)[] = [];
    worker.use(
      ...shellHandlers,
      projectList([]),
      http.get("/v1/console/organizations", ({ request, response }) => {
        const url = new URL(request.url);
        queries.push(url.searchParams.get("query"));
        expect(url.searchParams.get("limit")).toBe("20");
        return response(200).json({ items: [organization, second], nextCursor: null, total: null });
      }),
    );
    const screen = await renderWithProviders(`/${testOrgId}`);

    await screen.getByRole("button", { name: "Switch organization: Acme" }).click();
    await expect.element(screen.getByRole("option", { name: "Globex" })).toBeVisible();
    await userEvent.fill(screen.getByRole("searchbox", { name: "Search organizations" }), "glo");

    await expect.poll(() => queries).toEqual([null, "glo"]);
  });

  it("0017 CNS-5, ORG-1: New organization is a one field dialog that lands in it", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      projectList([]),
      sessionRead,
      http.get("/v1/console/organizations", ({ response }) =>
        response(200).json({ items: [organization], nextCursor: null, total: null }),
      ),
      http.post("/v1/console/organizations", async ({ request, response }) => {
        sent.push(await request.clone().json());
        return response(201).json(second);
      }),
    );
    const screen = await renderWithProviders(`/${testOrgId}`);

    await screen.getByRole("button", { name: "Switch organization: Acme" }).click();
    await screen.getByRole("link", { name: "New organization" }).click();
    const dialog = screen.getByRole("dialog", { name: "New organization" });
    await userEvent.fill(dialog.getByRole("textbox", { name: "Name" }), "Globex");
    await dialog.getByRole("button", { name: "Create organization" }).click();

    await expect.poll(() => screen.router.state.location.pathname).toBe(`/${second.id}`);
    await expect.element(screen.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
    expect(sent).toEqual([{ name: "Globex" }]);
  });

  it("0017 CNS-5, ORG-3, AC-9: rename is a one field dialog that sends the version", async () => {
    const sent: { body: unknown; ifMatch: string | null }[] = [];
    worker.use(
      ...shellHandlers,
      sessionRead,
      http.patch("/v1/console/organizations/{organizationId}", async ({ request, response }) => {
        sent.push({ body: await request.clone().json(), ifMatch: request.headers.get("If-Match") });
        return response(200).json({ ...organization, name: "Acme Corp" });
      }),
    );
    const screen = await renderWithProviders(`/${testOrgId}/settings`);
    await expect.element(screen.getByRole("button", { name: "Rename" })).toBeVisible();
    await expectNoAxeViolations(document.body);

    await screen.getByRole("button", { name: "Rename" }).click();
    const dialog = screen.getByRole("dialog", { name: "Rename organization" });
    await userEvent.fill(dialog.getByRole("textbox", { name: "Name" }), "Acme Corp");
    await dialog.getByRole("button", { name: "Rename" }).click();

    await expect
      .element(screen.getByRole("button", { name: "Switch organization: Acme Corp" }))
      .toBeVisible();
    expect(sent).toEqual([{ body: { name: "Acme Corp" }, ifMatch: `"${organization.version}"` }]);
  });

  it("0001 UX-30: an organization the operator can't change keeps its button, unavailable", async () => {
    worker.use(
      http.get("/v1/console/organizations/{organizationId}", ({ response }) =>
        response(200).json({ ...organization, allowedActions: ["org.read"] }),
      ),
      ...shellHandlers,
    );
    const screen = await renderWithProviders(`/${testOrgId}/settings`);

    await expect
      .element(screen.getByRole("button", { name: "Rename" }))
      .toHaveAttribute("aria-disabled", "true");
  });

  it("0017 CNS-4: the sidebar shows Projects and Organization settings at this scope", async () => {
    worker.use(...shellHandlers, projectList([testProject]));
    const screen = await renderWithProviders(`/${testOrgId}`);

    await screen.getByRole("button", { name: "Open navigation" }).click();
    await expect.element(screen.getByRole("link", { name: "Organization settings" })).toBeVisible();
  });

  it("0017 CNS-5, QRY-10: the page polls while a project is deleting, until it's gone", async () => {
    let reads = 0;
    worker.use(
      ...shellHandlers,
      http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) => {
        reads += 1;
        return response(200).json({
          items: reads === 1 ? [{ ...testProject, status: "deleting" }] : [],
          nextCursor: null,
          total: null,
        });
      }),
    );
    const screen = await renderWithProviders(`/${testOrgId}`);

    await expect.element(screen.getByText("Deleting")).toBeVisible();
    await expect
      .element(screen.getByRole("heading", { name: "Create your first project" }), {
        timeout: 4000,
      })
      .toBeVisible();
  });
});
