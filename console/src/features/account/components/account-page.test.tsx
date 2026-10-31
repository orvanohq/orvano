import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { i18n } from "@/shared/i18n";
import { announceSessionChanged, signOutEverywhere } from "@/shared/session-channel";
import { expectNoAxeViolations } from "@/test/axe";
import { http, problem, shellHandlers, testSession } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

vi.mock("@/shared/session-channel", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/session-channel")>()),
  announceSessionChanged: vi.fn<() => void>(),
  signOutEverywhere: vi.fn<() => Promise<void>>(),
}));

afterEach(() => {
  vi.mocked(announceSessionChanged).mockClear();
  vi.mocked(signOutEverywhere).mockClear();
});

const saved = { ...testSession.operator, version: "AAAAAAAAAAI" };

function fieldProblem(field: string, code: string) {
  return Response.json(
    {
      type: "about:blank",
      title: "Problem",
      status: 400,
      detail: "A field in the request is not valid.",
      code: "validation_failed",
      errors: [{ field, location: "body", code, message: "Refused." }],
    },
    { status: 400, headers: { "Content-Type": "application/problem+json" } },
  );
}

function cardOf(screen: Awaited<ReturnType<typeof renderWithProviders>>, name: string) {
  return screen.getByRole("region", { name });
}

// Spec 0017 ACC-1 to ACC-3, AC-8, AC-19.
describe("the account page", () => {
  it("0017 ACC-3: shows the profile, language, password, and sign out cards, and the account menu links here", async () => {
    worker.use(
      ...shellHandlers,
      http.get("/v1/console/organizations/{organizationId}/projects", ({ response }) =>
        response(200).json({ items: [], nextCursor: null, total: null }),
      ),
    );
    const screen = await renderWithProviders("/");

    await screen.getByRole("button", { name: "Account" }).click();
    await screen.getByRole("menuitem", { name: "Account" }).click();

    await expect.element(screen.getByRole("heading", { level: 1, name: "Account" })).toBeVisible();
    expect(screen.router.state.resolvedLocation?.pathname).toBe("/account");
    expect(document.title).toBe("Account · Orvano");
    for (const name of ["Profile", "Language", "Password", "Sign out"]) {
      await expect.element(cardOf(screen, name)).toBeVisible();
    }
    await expectNoAxeViolations();
  });

  it("0017 ACC-1, 0003 QRY-6: saves the name with the session's version and updates the session", async () => {
    const seen: { body: unknown; ifMatch: string | null }[] = [];
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", async ({ request, response }) => {
        seen.push({ body: await request.clone().json(), ifMatch: request.headers.get("If-Match") });
        return response(200).json({ ...saved, name: "Grace Owner" });
      }),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Grace Owner");
    await cardOf(screen, "Profile").getByRole("button", { name: "Update" }).click();

    await vi.waitFor(() => {
      expect(seen).toEqual([
        {
          body: { name: "Grace Owner", email: "ada@example.com" },
          ifMatch: `"${testSession.operator.version}"`,
        },
      ]);
    });
    await expect
      .element(cardOf(screen, "Profile").getByRole("button", { name: "Update" }))
      .toHaveAttribute("aria-disabled", "true");
    expect(screen.queryClient.getQueryData(["session"])).toMatchObject({
      operator: { name: "Grace Owner", version: saved.version },
    });
  });

  it("0017 ACC-1, AC-8: a changed email asks for the password on reauth_required, then retries once", async () => {
    let attempts = 0;
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", ({ response }) => {
        attempts += 1;
        return attempts === 1
          ? problem(403, "reauth_required")
          : response(200).json({ ...saved, email: "grace@example.com" });
      }),
      http.post("/v1/console/session/reauth", ({ response }) => response(204).empty()),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(screen.getByRole("textbox", { name: "Email" }), "grace@example.com");
    await cardOf(screen, "Profile").getByRole("button", { name: "Update" }).click();
    const dialog = screen.getByRole("dialog", { name: "Confirm your password" });
    await userEvent.fill(dialog.getByLabelText("Password", { exact: true }), "correct horse");
    await dialog.getByRole("button", { name: "Confirm" }).click();

    await expect.element(dialog).not.toBeInTheDocument();
    await vi.waitFor(() => {
      expect(attempts).toBe(2);
    });
  });

  it("0017 ACC-1: a taken email fails on the field", async () => {
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", () => fieldProblem("email", "not_unique")),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(screen.getByRole("textbox", { name: "Email" }), "taken@example.com");
    await cardOf(screen, "Profile").getByRole("button", { name: "Update" }).click();

    await expect.element(screen.getByText("This email is taken.").first()).toBeVisible();
  });

  it("0014 CNC-3: a stale version says to reload", async () => {
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", () => problem(412, "version_conflict")),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Grace Owner");
    await cardOf(screen, "Profile").getByRole("button", { name: "Update" }).click();

    await expect
      .element(screen.getByText("Reload the page, then try again.").first())
      .toBeVisible();
  });

  it("0017 AC-19: the language saves, and Use my browser sends null", async () => {
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.patch("/v1/console/account", async ({ request, response }) => {
        const body = (await request.clone().json()) as { locale: string | null };
        sent.push(body);
        return response(200).json({ ...saved, locale: body.locale });
      }),
    );
    const screen = await renderWithProviders("/account", {
      session: { ...testSession, operator: { ...testSession.operator, locale: "en" } },
    });
    const card = cardOf(screen, "Language");

    await card.getByRole("button", { name: /English/ }).click();
    await screen.getByRole("option", { name: "Use my browser" }).click();
    await card.getByRole("button", { name: "Update" }).click();

    await vi.waitFor(() => {
      expect(sent).toEqual([{ locale: null }]);
    });
    expect(i18n.locale).toBe("en");
  });

  it("0017 ACC-2, AC-19: a password change keeps the new CSRF token, tells the other tabs, and empties the fields", async () => {
    const rotated = { ...testSession, csrfToken: "bmV3LWNzcmYtdG9rZW4" };
    const sent: unknown[] = [];
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/account/password", async ({ request, response }) => {
        sent.push(await request.clone().json());
        return response(200).json(rotated);
      }),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(
      screen.getByLabelText("Current password", { exact: true }),
      "correct horse",
    );
    await userEvent.fill(
      screen.getByLabelText("New password", { exact: true }),
      "a brand new passphrase",
    );
    await screen.getByRole("button", { name: "Change password" }).click();

    await expect.element(screen.getByText("Password changed")).toBeVisible();
    expect(sent).toEqual([
      { currentPassword: "correct horse", newPassword: "a brand new passphrase" },
    ]);
    expect(screen.queryClient.getQueryData(["session"])).toMatchObject({
      csrfToken: rotated.csrfToken,
    });
    expect(announceSessionChanged).toHaveBeenCalledOnce();
    await expect
      .element(screen.getByLabelText("Current password", { exact: true }))
      .toHaveValue("");
    await expect.element(screen.getByLabelText("New password", { exact: true })).toHaveValue("");
  });

  it("0017 ACC-2: a wrong current password fails on its field, and a refused new one on its own", async () => {
    worker.use(
      ...shellHandlers,
      http.post("/v1/console/account/password", async ({ request }) => {
        const { currentPassword } = (await request.clone().json()) as { currentPassword: string };
        return currentPassword === "not it"
          ? fieldProblem("currentPassword", "invalid_password")
          : fieldProblem("newPassword", "password_too_common");
      }),
    );
    const screen = await renderWithProviders("/account");

    await userEvent.fill(screen.getByLabelText("Current password", { exact: true }), "not it");
    await userEvent.fill(screen.getByLabelText("New password", { exact: true }), "sunshine");
    await screen.getByRole("button", { name: "Change password" }).click();
    await expect.element(screen.getByText("That password is wrong.").first()).toBeVisible();

    await userEvent.fill(
      screen.getByLabelText("Current password", { exact: true }),
      "correct horse",
    );
    await screen.getByRole("button", { name: "Change password" }).click();
    await expect
      .element(screen.getByText("This password is too common. Choose another.").first())
      .toBeVisible();
    expect(announceSessionChanged).not.toHaveBeenCalled();
  });

  it("0017 ACC-3: Sign out signs out everywhere", async () => {
    worker.use(...shellHandlers);
    const screen = await renderWithProviders("/account");

    await cardOf(screen, "Sign out").getByRole("button", { name: "Sign out" }).click();

    expect(signOutEverywhere).toHaveBeenCalledOnce();
  });
});
