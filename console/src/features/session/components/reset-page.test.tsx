import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { http, problem } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

const TOKEN = "orv_reset_dGhpcyBpcyBhIHRlc3QgdG9rZW4gb2YgMzIgYnl0ZXMgbG9uZw";
const PASSWORD = "a brand new passphrase";

let startUrl = "";

function openLink() {
  window.history.replaceState(window.history.state, "", `${startUrl}#token=${TOKEN}`);
}

beforeEach(() => {
  startUrl = `${window.location.pathname}${window.location.search}`;
});

afterEach(() => {
  window.history.replaceState(window.history.state, "", startUrl);
});

async function submit(screen: Awaited<ReturnType<typeof renderWithProviders>>) {
  await userEvent.fill(screen.getByLabelText("New password", { exact: true }), PASSWORD);
  await screen.getByRole("button", { name: "Change password" }).click();
}

// Spec 0017 RST-2, RST-3, AC-18.
describe("the reset page", () => {
  it("0017 RST-2, 0003 URL-5: takes the token from the fragment once, and sends it with the new password", async () => {
    const sent: unknown[] = [];
    worker.use(
      http.post("/v1/console/password-resets", async ({ request, response }) => {
        sent.push(await request.clone().json());
        return response(204).empty();
      }),
    );
    openLink();
    const screen = await renderWithProviders("/reset", { strict: true });

    await expect
      .element(screen.getByRole("heading", { level: 1, name: "Reset your password" }))
      .toBeVisible();
    expect(window.location.hash).toBe("");
    await expectNoAxeViolations();

    await submit(screen);

    await expect.element(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
    expect(sent).toEqual([{ token: TOKEN, password: PASSWORD }]);
  });

  it("0017 RST-3: leaves a session this browser held behind, and goes to sign in", async () => {
    worker.use(http.post("/v1/console/password-resets", ({ response }) => response(204).empty()));
    openLink();
    const screen = await renderWithProviders("/reset");

    await submit(screen);

    await expect.element(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeVisible();
    expect(screen.queryClient.getQueryData(["session"])).toBeNull();
  });

  it("0017 RST-2: with no token it shows the command that makes a link", async () => {
    const screen = await renderWithProviders("/reset", { session: null });

    await expect
      .element(screen.getByText("orvano admin reset-operator --email <email>"))
      .toBeVisible();
    await expect
      .element(screen.getByRole("button", { name: "Change password" }))
      .not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("0017 AC-18: a refused link says so, once, and shows the command", async () => {
    worker.use(http.post("/v1/console/password-resets", () => problem(401, "reset_token_invalid")));
    openLink();
    const screen = await renderWithProviders("/reset", { session: null });

    await submit(screen);

    await expect.element(screen.getByText("This link isn't valid any more.")).toBeVisible();
    await expect
      .element(screen.getByText("orvano admin reset-operator --email <email>"))
      .toBeVisible();
  });

  it("0017 PSW-1: a refused password fails on the field and keeps the link", async () => {
    worker.use(
      http.post("/v1/console/password-resets", () =>
        Response.json(
          {
            type: "about:blank",
            title: "Problem",
            status: 400,
            detail: "A field in the request is not valid.",
            code: "validation_failed",
            errors: [
              {
                field: "password",
                location: "body",
                code: "password_too_common",
                message: "Too common.",
              },
            ],
          },
          { status: 400, headers: { "Content-Type": "application/problem+json" } },
        ),
      ),
    );
    openLink();
    const screen = await renderWithProviders("/reset", { session: null });

    await submit(screen);

    await expect
      .element(screen.getByText("This password is too common. Choose another.").first())
      .toBeVisible();
    await expect.element(screen.getByLabelText("New password", { exact: true })).toBeVisible();
  });

  it("0017 RST-2: a new link opened on the page takes over from a refused one", async () => {
    worker.use(http.post("/v1/console/password-resets", () => problem(401, "reset_token_invalid")));
    openLink();
    const screen = await renderWithProviders("/reset", { session: null });
    await submit(screen);
    await expect.element(screen.getByText("This link isn't valid any more.")).toBeVisible();

    openLink();
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    await expect.element(screen.getByLabelText("New password", { exact: true })).toBeVisible();
    expect(window.location.hash).toBe("");
  });
});
