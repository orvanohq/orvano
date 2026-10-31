import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
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

const page = (items: (typeof testProject)[]) =>
  http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) =>
    response(200).json({ items, nextCursor: null, total: null }),
  );

// Spec 0017 CNS-3, CNS-5, CNS-6, AC-10, AC-21.
describe("the organization page", () => {
  it("0017 CNS-5: the first use state leads to New project", async () => {
    worker.use(...shellHandlers, page([]));
    const screen = await renderWithProviders(`/${testOrgId}`);

    await expect
      .element(screen.getByRole("heading", { name: "Create your first project" }))
      .toBeVisible();
    await expect.element(screen.getByRole("button", { name: "New project" })).toBeVisible();
    await expectNoAxeViolations(document.body);
  });

  it("0017 CNS-3: the shell shows the organization from real data", async () => {
    worker.use(...shellHandlers, page([testProject]));
    const screen = await renderWithProviders(`/${testOrgId}`);

    await expect
      .element(screen.getByRole("button", { name: "Switch organization: Acme" }))
      .toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Shop" })).toBeVisible();
    expect(document.title).toBe("Projects · Orvano");
  });

  it("0017 CNS-5, AC-10: New project sends the name and origins, and opens the project", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      page([testProject]),
      http.post("/v1/console/organizations/{organizationId}/projects", async ({ request }) => {
        sent.push(await request.clone().json());
        return Response.json(testProject, { status: 201 });
      }),
    );
    const screen = await renderWithProviders(`/${testOrgId}`);

    await screen.getByRole("button", { name: "New project" }).click();
    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Shop");
    await userEvent.fill(
      screen.getByRole("textbox", { name: /^Allowed origins/ }),
      "https://app.example.com\n\nhttp://localhost:3000\n",
    );
    await screen.getByRole("button", { name: "Create project" }).click();

    await expect.element(screen.getByRole("heading", { level: 1, name: "Shop" })).toBeVisible();
    expect(sent).toEqual([
      { name: "Shop", allowedOrigins: ["https://app.example.com", "http://localhost:3000"] },
    ]);
    expect(screen.router.state.location.pathname).toBe(`/${testOrgId}/${testProjectId}`);
  });

  it("0017 AC-9: an organization the operator can't see shows the shell's not found state", async () => {
    worker.use(
      http.get("/v1/console/organizations/{organizationId}", () => problem(404, "not_found")),
      page([]),
    );
    const screen = await renderWithProviders(`/${testOrgId}`);

    await expect.element(screen.getByRole("heading", { name: /not found/i })).toBeVisible();
  });
});

describe("the project overview", () => {
  it("0017 CNS-6: shows the id, the address, the first use action, and every SDK", async () => {
    worker.use(...shellHandlers);
    const screen = await renderWithProviders(`/${testOrgId}/${testProjectId}`);

    await expect
      .element(screen.getByRole("textbox", { name: "Project ID" }))
      .toHaveValue(testProjectId);
    await expect
      .element(screen.getByRole("textbox", { name: "API address" }))
      .toHaveValue("https://orvano.example.com");
    await expect.element(screen.getByRole("heading", { name: "Create an API key" })).toBeVisible();
    await expect
      .element(screen.getByRole("link", { name: "Create API key" }))
      .toHaveAttribute("href", expect.stringContaining("/settings/keys"));
    for (const name of ["JavaScript", "Node.js", "Next.js", "Dart", "Flutter", ".NET"]) {
      // oxlint-disable-next-line no-await-in-loop -- one assertion at a time reads better in a failure
      await expect.element(screen.getByRole("tab", { name })).toBeVisible();
    }
  });
});

// Spec 0001 UX-14, spec 0014 PAG-2: a project past the first page can be reached.
describe("paging the organization page", () => {
  const second = { ...testProject, id: "01970000-0000-7000-8000-00000000000f", name: "Bakery" };
  const pages = http.get(
    "/v1/console/organizations/{organizationId}/projects",
    ({ request, response }) =>
      new URL(request.url).searchParams.get("cursor") === "page-2"
        ? response(200).json({ items: [second], nextCursor: null, total: null })
        : response(200).json({ items: [testProject], nextCursor: "page-2", total: null }),
  );

  it("0001 UX-14: Next and Previous move between pages, and a dialog keeps the page", async () => {
    worker.use(pages, ...shellHandlers);
    const screen = await renderWithProviders(`/${testOrgId}`);

    await screen.getByRole("button", { name: "Next" }).click();
    await expect.element(screen.getByRole("link", { name: "Bakery" })).toBeVisible();
    expect(screen.router.state.location.search).toEqual({ cursor: "page-2" });

    await screen.getByRole("button", { name: "New project" }).click();
    await screen.getByRole("button", { name: "Cancel" }).click();
    await expect.element(screen.getByRole("link", { name: "Bakery" })).toBeVisible();
    expect(screen.router.state.location.search).toEqual({ cursor: "page-2" });

    await screen.getByRole("button", { name: "Previous" }).click();
    await expect.element(screen.getByRole("link", { name: "Shop" })).toBeVisible();
    await expectNoAxeViolations(document.body);
  });

  it("0017 CNS-5: an empty later page is no first use: Previous stays there", async () => {
    worker.use(page([]), ...shellHandlers);
    const screen = await renderWithProviders(`/${testOrgId}?cursor=page-2`);

    await expect.element(screen.getByRole("button", { name: "Previous" })).toBeVisible();
    await expect
      .element(screen.getByRole("heading", { name: "Create your first project" }))
      .not.toBeInTheDocument();
  });
});
