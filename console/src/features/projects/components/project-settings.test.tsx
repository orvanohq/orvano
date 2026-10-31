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
import { projectQuery, type Project } from "../api/queries";

const settings = `/${testOrgId}/${testProjectId}/settings`;

const projectList = (items: Project[]) =>
  http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) =>
    response(200).json({ items, nextCursor: null, total: null }),
  );

// Spec 0017 CNS-1, PJT-5, PJT-7, AC-8, AC-11, AC-12, AC-21.
describe("project settings", () => {
  it("0017 CNS-1: settings opens on General, with a tab for each page", async () => {
    worker.use(...shellHandlers);
    const screen = await renderWithProviders(settings);

    await expect.element(screen.getByRole("heading", { name: "Project settings" })).toBeVisible();
    await expect
      .element(screen.getByRole("link", { name: "General" }))
      .toHaveAttribute("aria-current", "page");
    await expect.element(screen.getByRole("link", { name: "API keys" })).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "Allowed origins" })).toBeVisible();
    expect(screen.router.state.location.pathname).toBe(`${settings}/general`);
    await expectNoAxeViolations(document.body);
  });

  it("0017 PJT-5, AC-11: a rename sends the version it read, once something changed", async () => {
    const sent: { body: unknown; ifMatch: string | null }[] = [];
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/projects/{projectId}", async ({ request, response }) => {
        sent.push({
          body: await request.clone().json(),
          ifMatch: request.headers.get("If-Match"),
        });
        return response(200).json({ ...testProject, name: "Renamed", version: "AAAAAAAAAAI" });
      }),
    );
    const screen = await renderWithProviders(`${settings}/general`);

    await expect
      .element(screen.getByRole("button", { name: "Update" }))
      .toHaveAttribute("aria-disabled", "true");
    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Renamed");
    await screen.getByRole("button", { name: "Update" }).click();

    await expect
      .poll(() => sent)
      .toEqual([{ body: { name: "Renamed" }, ifMatch: `"${testProject.version}"` }]);
    await expect
      .poll(() => screen.queryClient.getQueryData(projectQuery(testProjectId).queryKey)?.name)
      .toBe("Renamed");
  });

  it("0014 CNC-3: a stale version says so and keeps what was typed", async () => {
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/projects/{projectId}", () => problem(412, "version_conflict")),
    );
    const screen = await renderWithProviders(`${settings}/general`);

    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Renamed");
    await screen.getByRole("button", { name: "Update" }).click();

    await expect
      .element(screen.getByText("Someone else changed this project", { exact: false }).first())
      .toBeVisible();
    await expect.element(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Renamed");
  });

  it("0017 AC-11: the origins page saves one origin per line", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/projects/{projectId}", async ({ request, response }) => {
        sent.push(await request.clone().json());
        return response(200).json({
          ...testProject,
          allowedOrigins: ["https://app.example.com", "http://localhost:3000"],
          version: "AAAAAAAAAAI",
        });
      }),
    );
    const screen = await renderWithProviders(`${settings}/origins`);
    await expect.element(screen.getByRole("textbox", { name: /Origins/ })).toBeVisible();
    await expectNoAxeViolations(document.body);

    await userEvent.fill(
      screen.getByRole("textbox", { name: /Origins/ }),
      "https://app.example.com\n\nhttp://localhost:3000\n",
    );
    await screen.getByRole("button", { name: "Update" }).click();

    await expect
      .poll(() => sent)
      .toEqual([{ allowedOrigins: ["https://app.example.com", "http://localhost:3000"] }]);
    await expect
      .element(screen.getByRole("button", { name: "Update" }))
      .toHaveAttribute("aria-disabled", "true");
  });

  it("0017 PJT-6: a refused origin is named at the field", async () => {
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/projects/{projectId}", () =>
        Response.json(
          {
            type: "about:blank",
            title: "Problem",
            status: 400,
            detail: "A field in the request is not valid.",
            code: "validation_failed",
            errors: [
              {
                field: "allowedOrigins[1]",
                location: "body",
                code: "invalid_origin",
                message: "Use an exact origin.",
              },
            ],
          },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    const screen = await renderWithProviders(`${settings}/origins`);

    await userEvent.fill(
      screen.getByRole("textbox", { name: /Origins/ }),
      "https://ok.example.com\nhttps://*.example.com",
    );
    await screen.getByRole("button", { name: "Update" }).click();

    await expect
      .element(
        screen.getByText("https://*.example.com isn't an exact origin", { exact: false }).first(),
      )
      .toBeVisible();
  });

  it("0017 PJT-7, AC-12, UX-23: delete asks for the name, lists what goes, and goes to the list", async () => {
    let deleted = false;
    const deleting: Project = { ...testProject, status: "deleting" };
    worker.use(
      ...shellHandlers,
      projectList([deleting]),
      http.get("/v1/console/projects/{projectId}/dependents", ({ response }) =>
        response(200).json({ items: [{ kind: "api_key", count: 3, estimated: false }] }),
      ),
      http.delete("/v1/console/projects/{projectId}", ({ response }) => {
        deleted = true;
        return response(202).json(deleting);
      }),
    );
    const screen = await renderWithProviders(`${settings}/general`);

    await screen.getByRole("button", { name: "Delete project" }).click();
    const dialog = screen.getByRole("alertdialog", { name: "Delete Shop?" });
    await expect.element(dialog.getByText("3 API keys")).toBeVisible();
    const confirm = dialog.getByRole("button", { name: "Delete project" });
    await expect.element(confirm).toHaveAttribute("aria-disabled", "true");
    await userEvent.fill(dialog.getByRole("textbox"), "Shop");
    await confirm.click();

    await expect.element(screen.getByText("Deleting")).toBeVisible();
    expect(deleted).toBe(true);
    expect(screen.router.state.location.pathname).toBe(`/${testOrgId}`);
  });

  it("0017 AC-8: delete asks for the password, then retries", async () => {
    let deletes = 0;
    const deleting: Project = { ...testProject, status: "deleting" };
    worker.use(
      ...shellHandlers,
      projectList([deleting]),
      http.get("/v1/console/projects/{projectId}/dependents", ({ response }) =>
        response(200).json({ items: [] }),
      ),
      http.delete("/v1/console/projects/{projectId}", ({ response }) => {
        deletes += 1;
        return deletes === 1 ? problem(403, "reauth_required") : response(202).json(deleting);
      }),
      http.post("/v1/console/session/reauth", () => new Response(null, { status: 204 })),
    );
    const screen = await renderWithProviders(`${settings}/general`);

    await screen.getByRole("button", { name: "Delete project" }).click();
    const dialog = screen.getByRole("alertdialog", { name: "Delete Shop?" });
    await userEvent.fill(dialog.getByRole("textbox"), "Shop");
    await dialog.getByRole("button", { name: "Delete project" }).click();
    const prompt = screen.getByRole("dialog", { name: "Confirm your password" });
    await userEvent.fill(prompt.getByLabelText("Password"), "right password");
    await prompt.getByRole("button", { name: "Confirm" }).click();

    await expect.element(screen.getByText("Deleting")).toBeVisible();
    expect(deletes).toBe(2);
  });

  it("0001 UX-30: without project.delete the button stays visible and says why", async () => {
    worker.use(
      http.get("/v1/console/projects/{projectId}", ({ response }) =>
        response(200).json({ ...testProject, allowedActions: ["project.read"] }),
      ),
      ...shellHandlers,
    );
    const screen = await renderWithProviders(`${settings}/general`);

    await expect
      .element(screen.getByRole("button", { name: "Delete project" }))
      .toHaveAttribute("aria-disabled", "true");
  });
});
