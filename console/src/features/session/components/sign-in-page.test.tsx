import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { safeRedirect } from "@/features/session";
import {
  http,
  problem,
  shellHandlers,
  testOrgId,
  testProjectId,
  testSession,
} from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

const emptyProjects = http.get(
  "/v1/console/organizations/{organizationId}/projects",
  ({ response }) => response(200).json({ items: [], nextCursor: null, total: null }),
);

async function signIn(screen: Awaited<ReturnType<typeof renderWithProviders>>) {
  await userEvent.fill(screen.getByRole("textbox", { name: "Email" }), "ada@example.com");
  await userEvent.fill(screen.getByLabelText("Password", { exact: true }), "correct horse");
  await screen.getByRole("button", { name: "Sign in" }).click();
}

// Spec 0017 SES-3, CNS-2, AC-5, AC-6.
describe("the sign in page", () => {
  it("0017 AC-6: a signed out visit goes to sign in, keeps the path, and comes back after", async () => {
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/session", () => Response.json(testSession, { status: 201 })),
    );
    const path = `/${testOrgId}/${testProjectId}/settings/keys`;
    const screen = await renderWithProviders(path, { session: null });

    await expect.element(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
    await signIn(screen);

    await expect.element(screen.getByRole("heading", { level: 1, name: "API keys" })).toBeVisible();
    expect(screen.router.state.location.pathname).toBe(path);
  });

  it("0017 CNS-1: a signed in operator lands on their first organization", async () => {
    worker.use(...shellHandlers, emptyProjects);
    const screen = await renderWithProviders("/");

    await expect.element(screen.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
    expect(screen.router.state.location.pathname).toBe(`/${testOrgId}`);
  });

  it("0017 CNS-2: a signed in operator on /sign-in goes home", async () => {
    worker.use(...shellHandlers, emptyProjects);
    const screen = await renderWithProviders("/sign-in");

    await expect.element(screen.getByRole("heading", { level: 1, name: "Projects" })).toBeVisible();
  });

  it("0017 BOT-1: the page and its title use the instance name", async () => {
    const screen = await renderWithProviders("/sign-in", {
      session: null,
      config: { instanceName: "Staging" },
    });

    await expect.element(screen.getByText("Staging")).toBeVisible();
    expect(document.title).toBe("Sign in · Staging");
  });

  it("0017 AC-5: wrong details say so, without saying which", async () => {
    worker.use(http.post("/v1/console/session", () => problem(401, "invalid_credentials")));
    const screen = await renderWithProviders("/sign-in", { session: null });

    await signIn(screen);

    await expect.element(screen.getByText("The email or password is wrong.")).toBeVisible();
  });

  it("0017 AC-5: a throttled sign in says when to come back", async () => {
    worker.use(
      http.post("/v1/console/session", () => problem(429, "rate_limited", { retryAfter: 90 })),
    );
    const screen = await renderWithProviders("/sign-in", { session: null });

    await signIn(screen);

    await expect.element(screen.getByText("Too many tries. Try again in 2 minutes.")).toBeVisible();
  });

  it.each([
    ["/acme/settings?tab=keys", "/acme/settings?tab=keys"],
    ["//evil.example", undefined],
    ["/\\evil.example", undefined],
    ["https://evil.example", undefined],
    [7, undefined],
  ])("0017 AC-6: redirect %s stays on this origin", (value, expected) => {
    expect(safeRedirect(value)).toBe(expected);
  });
});
