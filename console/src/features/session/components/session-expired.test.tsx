import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { reloadConsole } from "@/shared/session-channel";
import { sessionEnded } from "@/shared/stores/session";
import { expectNoAxeViolations } from "@/test/axe";
import { http, problem, shellHandlers, testSession } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

vi.mock("@/shared/session-channel", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/shared/session-channel")>()),
  reloadConsole: vi.fn<() => void>(),
}));

afterEach(() => {
  sessionEnded.set(false);
  vi.mocked(reloadConsole).mockClear();
});

const operator = { ...testSession.operator, name: "Grace Owner", version: "AAAAAAAAAAI" };

function expiredOnce(sent: unknown[]) {
  return http.patch("/v1/console/account", async ({ request, response }) => {
    sent.push(await request.clone().json());
    return sent.length === 1 ? problem(401, "session_expired") : response(200).json(operator);
  });
}

async function saveName(screen: Awaited<ReturnType<typeof renderWithProviders>>) {
  await userEvent.fill(screen.getByRole("textbox", { name: "Name" }), "Grace Owner");
  await screen.getByRole("button", { name: "Update" }).first().click();
}

/** A plain call with no `withReauth` around it, so no write is left waiting for the next test. */
async function changePassword(screen: Awaited<ReturnType<typeof renderWithProviders>>) {
  await userEvent.fill(screen.getByLabelText("Current password", { exact: true }), "correct horse");
  await userEvent.fill(
    screen.getByLabelText("New password", { exact: true }),
    "a brand new passphrase",
  );
  await screen.getByRole("button", { name: "Change password" }).click();
}

const expiredPassword = (code: string) =>
  http.post("/v1/console/account/password", () => problem(401, code));

// Spec 0017 SES-9, AC-6, spec 0003 QRY-8, spec 0001 UX-22.
describe("the session expired dialog", () => {
  it("0017 SES-9, 0003 QRY-8: opens over the page, keeps the form, and the write retries once after sign in", async () => {
    const sent: unknown[] = [];
    const signedIn = { ...testSession, csrfToken: "bmV3LWNzcmYtdG9rZW4" };
    const csrf: (string | null)[] = [];
    worker.use(
      ...shellHandlers,
      expiredOnce(sent),
      http.post("/v1/console/session", ({ request }) => {
        csrf.push(request.headers.get("Orvano-Csrf"));
        return Response.json(signedIn, { status: 201 });
      }),
    );
    const screen = await renderWithProviders("/account");

    await saveName(screen);
    const dialog = screen.getByRole("dialog", { name: "Your session ended" });
    await expect.element(dialog).toBeVisible();
    await expect
      .element(dialog.getByRole("textbox", { name: "Email" }))
      .toHaveValue("ada@example.com");
    await expectNoAxeViolations();

    await userEvent.fill(dialog.getByLabelText("Password", { exact: true }), "correct horse");
    await dialog.getByRole("button", { name: "Sign in" }).click();

    await expect.element(dialog).not.toBeInTheDocument();
    await vi.waitFor(() => {
      expect(sent).toHaveLength(2);
    });
    expect(sent[1]).toEqual(sent[0]);
    await expect.element(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Grace Owner");
    expect(screen.queryClient.getQueryData(["session"])).toMatchObject({
      csrfToken: signedIn.csrfToken,
      operator: { version: operator.version },
    });
    expect(reloadConsole).not.toHaveBeenCalled();
  });

  it("0003 QRY-8: a different operator gets a full reload, since the page holds the first one's data", async () => {
    const someoneElse = {
      ...testSession,
      operator: { ...testSession.operator, id: "01970000-0000-7000-8000-000000000002" },
    };
    worker.use(
      ...shellHandlers,
      expiredPassword("session_expired"),
      http.post("/v1/console/session", () => Response.json(someoneElse, { status: 201 })),
    );
    const screen = await renderWithProviders("/account");

    await changePassword(screen);
    const dialog = screen.getByRole("dialog", { name: "Your session ended" });
    await userEvent.fill(dialog.getByRole("textbox", { name: "Email" }), "grace@example.com");
    await userEvent.fill(dialog.getByLabelText("Password", { exact: true }), "correct horse");
    await dialog.getByRole("button", { name: "Sign in" }).click();

    await vi.waitFor(() => {
      expect(reloadConsole).toHaveBeenCalledOnce();
    });
  });

  it("0017 AC-5: a wrong password stays in the dialog, and the page behind it waits", async () => {
    worker.use(
      ...shellHandlers,
      expiredPassword("session_expired"),
      http.post("/v1/console/session", () => problem(401, "invalid_credentials")),
    );
    const screen = await renderWithProviders("/account");

    await changePassword(screen);
    const dialog = screen.getByRole("dialog", { name: "Your session ended" });
    await userEvent.fill(dialog.getByLabelText("Password", { exact: true }), "not it");
    await dialog.getByRole("button", { name: "Sign in" }).click();

    await expect.element(dialog.getByText("The email or password is wrong.").first()).toBeVisible();
    expect(sessionEnded.get()).toBe(true);
  });

  it("0017 SES-4: the plain 401 after the cookie went opens it too", async () => {
    worker.use(...shellHandlers, expiredPassword("unauthorized"));
    const screen = await renderWithProviders("/account");

    await changePassword(screen);

    await expect.element(screen.getByRole("dialog", { name: "Your session ended" })).toBeVisible();
  });

  it("0017 AC-5: a signed out visit opens no dialog, since there was no session to lose", async () => {
    worker.use(http.get("/v1/console/session", () => problem(401, "session_expired")));
    const screen = await renderWithProviders("/sign-in", { session: null });

    await expect.element(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
    expect(sessionEnded.get()).toBe(false);
  });
});
