import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { messages as sourceMessages } from "@/locales/en/messages.po";
import { activateLocale, getDirection, i18n } from "@/shared/i18n";
import { expectNoAxeViolations } from "@/test/axe";
import { deferred } from "@/test/deferred";
import { focusElement } from "@/test/focus";
import { http, problem } from "@/test/handlers";
import { worker } from "@/test/msw";
import { renderWithProviders } from "@/test/render";

// Spec 0016 FRS-4 to FRS-7, AC-8: the setup page's token step. The API answers through MSW (spec 0003 QA-2).

const TOKEN = "orv_setup_dGhpcyBpcyBhIHRlc3QgdG9rZW4gb2YgMzIgYnl0ZXM";
const setup = { config: { setupRequired: true } };

/** Records each token the page sends to the check endpoint, and answers with `answer` (204 by default). */
function checkEndpoint(
  answer: () => Response | Promise<Response> = () => new Response(null, { status: 204 }),
) {
  const sent: string[] = [];
  worker.use(
    http.post("/v1/console/setup/token", async ({ request }) => {
      sent.push((await request.json()).token);
      return answer();
    }),
  );
  return sent;
}

/** The address of the test page itself, where a setup link's fragment would be. */
function openWithFragment(fragment: string) {
  window.history.replaceState(
    window.history.state,
    "",
    `${window.location.pathname}${window.location.search}#${fragment}`,
  );
}

let startUrl = "";
beforeEach(() => {
  startUrl = `${window.location.pathname}${window.location.search}`;
});

afterEach(async () => {
  window.history.replaceState(window.history.state, "", startUrl);
  delete document.documentElement.dataset.theme;
  await activateLocale("en");
});

async function typeToken(screen: Awaited<ReturnType<typeof renderWithProviders>>, token: string) {
  const field = screen.getByRole("textbox", { name: "Setup token" });
  await focusElement(field);
  await userEvent.keyboard(token);
  return field;
}

describe("SetupPage", () => {
  it("0016 FRS-4: shows the heading, where the token comes from, the token field, and Continue", async () => {
    const screen = await renderWithProviders("/setup", setup);
    const main = screen.getByRole("main");

    await expect
      .element(main.getByRole("heading", { level: 1, name: "Set up Orvano" }))
      .toBeVisible();
    await expect.element(main.getByText(/from the install output or the server log/)).toBeVisible();
    await expect.element(main.getByText("orvano admin setup-token")).toBeVisible();
    await expect.element(main.getByRole("textbox", { name: "Setup token" })).toBeVisible();
    await expect.element(main.getByRole("button", { name: "Continue" })).toBeVisible();
  });

  it("0016 security model: the token field is kept from autofill, spell checking, and autocorrect", async () => {
    const screen = await renderWithProviders("/setup", setup);
    const field = screen.getByRole("textbox", { name: "Setup token" });

    await expect.element(field).toHaveAttribute("autocomplete", "off");
    await expect.element(field).toHaveAttribute("spellcheck", "false");
    await expect.element(field).toHaveAttribute("autocorrect", "off");
    await expect.element(field).toHaveAttribute("maxlength", "100");
  });

  it("0016 AC-8: a typed token, trimmed, is checked and accepted, and focus moves to the result", async () => {
    const sent = checkEndpoint();
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, `  ${TOKEN} `);
    await userEvent.keyboard("{Enter}");

    const accepted = screen.getByRole("heading", { level: 2, name: "Setup token accepted" });
    await expect.element(accepted).toHaveFocus();
    expect(sent).toEqual([TOKEN]);
    await expect
      .element(screen.getByRole("textbox", { name: "Setup token" }))
      .not.toBeInTheDocument();
  });

  it("0016 FRS-5: reads the token from the link's fragment once, drops it from the address, and checks it", async () => {
    const sent = checkEndpoint();
    openWithFragment(`token=${TOKEN}`);

    const screen = await renderWithProviders("/setup", setup);

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(window.location.hash).toBe("");
    expect(window.location.href).not.toContain(TOKEN);
    expect(sent).toEqual([TOKEN]);
  });

  it("0016 FRS-5: a link opened in a tab already on the page is taken from the fragment and checked", async () => {
    const sent = checkEndpoint();
    const screen = await renderWithProviders("/setup", { ...setup, strict: true });
    await expect.element(screen.getByRole("textbox", { name: "Setup token" })).toBeVisible();

    // Opening the link changes only the fragment: the browser fires hashchange, and the page stays loaded.
    openWithFragment(`token=${TOKEN}`);
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(window.location.hash).toBe("");
    expect(sent).toEqual([TOKEN]);
  });

  it("0016 FRS-5: under StrictMode the link's token is neither lost nor checked twice", async () => {
    const sent = checkEndpoint();
    openWithFragment(`token=${TOKEN}`);

    const screen = await renderWithProviders("/setup", { ...setup, strict: true });

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(sent).toEqual([TOKEN]);
    expect(window.location.hash).toBe("");
  });

  it("0016 FRS-5: keeps the token out of the query cache, the mutation cache, and the address", async () => {
    // Storage is checked end to end (e2e/setup.spec.ts): only src/shared/storage may touch it here (STO-1).
    checkEndpoint();
    openWithFragment(`token=${TOKEN}`);

    const screen = await renderWithProviders("/setup", setup);
    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();

    const cached = JSON.stringify([
      screen.queryClient
        .getQueryCache()
        .getAll()
        .map((query) => [query.queryKey, query.state.data]),
      screen.queryClient
        .getMutationCache()
        .getAll()
        .map((mutation) => mutation.state.variables),
    ]);
    expect(cached).not.toContain(TOKEN);
    expect(window.location.href).not.toContain(TOKEN);
  });

  it("0016 FRS-5: a fragment without a token changes nothing and checks nothing", async () => {
    const sent = checkEndpoint();
    openWithFragment("section=intro");

    const screen = await renderWithProviders("/setup", setup);

    await expect.element(screen.getByRole("textbox", { name: "Setup token" })).toHaveValue("");
    expect(window.location.hash).toBe("#section=intro");
    expect(sent).toEqual([]);
  });

  it("0016 FRS-5: a link with an empty token drops the fragment and asks for the token", async () => {
    const sent = checkEndpoint();
    openWithFragment("token=");

    const screen = await renderWithProviders("/setup", setup);

    await expect
      .element(screen.getByRole("textbox", { name: "Setup token" }))
      .toHaveAccessibleDescription("Enter the setup token.");
    expect(window.location.hash).toBe("");
    expect(sent).toEqual([]);
  });

  it("0016 FRS-5: a link with an overlong token is refused here, without a request", async () => {
    const sent = checkEndpoint();
    openWithFragment(`token=${"a".repeat(101)}`);

    const screen = await renderWithProviders("/setup", setup);

    await expect
      .element(screen.getByRole("textbox", { name: "Setup token" }))
      .toHaveAccessibleDescription("A setup token is at most 100 characters.");
    expect(window.location.hash).toBe("");
    expect(sent).toEqual([]);
  });

  it("0016 FRS-5: the token leaves the fragment, and what else the fragment holds stays", async () => {
    const sent = checkEndpoint();
    openWithFragment(`from=mail&token=${TOKEN}`);

    const screen = await renderWithProviders("/setup", setup);

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(window.location.hash).toBe("#from=mail");
    expect(sent).toEqual([TOKEN]);
  });

  it("0016 FRS-5: a link opened while a check runs leaves the address and replaces that check", async () => {
    const answer = deferred<Response>();
    const sent = checkEndpoint(() => answer.promise);
    openWithFragment(`token=${TOKEN}`);
    const screen = await renderWithProviders("/setup", setup);
    await expect
      .element(screen.getByRole("button", { name: "Continue" }))
      .toHaveAttribute("data-pending", "true");

    openWithFragment(`token=${TOKEN}2`);
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    await expect.poll(() => window.location.hash).toBe("");
    await expect.poll(() => sent.at(-1)).toBe(`${TOKEN}2`);
    answer.resolve(new Response(null, { status: 204 }));
    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
  });

  it("0016 FRS-5: a link opened on the accepted page leaves the address and replaces the accepted token", async () => {
    const answer = deferred<Response>();
    const sent = checkEndpoint(() =>
      sent.length === 1 ? new Response(null, { status: 204 }) : answer.promise,
    );
    openWithFragment(`token=${TOKEN}`);
    const screen = await renderWithProviders("/setup", setup);
    const accepted = screen.getByRole("heading", { level: 2, name: "Setup token accepted" });
    await expect.element(accepted).toBeVisible();

    openWithFragment(`token=${TOKEN}2`);
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    expect(window.location.hash).toBe("");
    await expect
      .element(screen.getByRole("button", { name: "Continue" }))
      .toHaveAttribute("data-pending", "true");
    await expect.poll(() => sent).toEqual([TOKEN, `${TOKEN}2`]);
    answer.resolve(new Response(null, { status: 204 }));
    await expect.element(accepted).toHaveFocus();
  });

  it("0016 FRS-5: a link that lands just as a token is accepted leaves the address too", async () => {
    const answer = deferred<Response>();
    const sent = checkEndpoint(() =>
      sent.length === 1 ? answer.promise : new Response(null, { status: 204 }),
    );
    openWithFragment(`token=${TOKEN}`);
    const screen = await renderWithProviders("/setup", setup);
    await expect.poll(() => sent).toEqual([TOKEN]);

    openWithFragment(`token=${TOKEN}2`);
    answer.resolve(new Response(null, { status: 204 }));

    await expect.poll(() => window.location.hash).toBe("");
    await expect.poll(() => sent).toEqual([TOKEN, `${TOKEN}2`]);
    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
  });

  it("0016 FRS-5: a rejected link on the accepted page shows the error on its token", async () => {
    const sent = checkEndpoint(() =>
      sent.length === 1 ? new Response(null, { status: 204 }) : problem(401, "setup_token_invalid"),
    );
    openWithFragment(`token=${TOKEN}`);
    const screen = await renderWithProviders("/setup", setup);
    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();

    openWithFragment(`token=${TOKEN}2`);
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    const field = screen.getByRole("textbox", { name: "Setup token" });
    await expect.element(field).toHaveAttribute("aria-invalid", "true");
    await expect.element(field).toHaveValue(`${TOKEN}2`);
    expect(window.location.hash).toBe("");
  });

  it("0016 FRS-5: on the accepted page, an empty token leaves the address and a fragment without one stays", async () => {
    const sent = checkEndpoint();
    openWithFragment(`token=${TOKEN}`);
    const screen = await renderWithProviders("/setup", setup);
    const accepted = screen.getByRole("heading", { level: 2, name: "Setup token accepted" });
    await expect.element(accepted).toBeVisible();

    openWithFragment("from=mail&token=");
    window.dispatchEvent(new HashChangeEvent("hashchange"));

    expect(window.location.hash).toBe("#from=mail");
    await expect.element(accepted).toBeVisible();
    expect(sent).toEqual([TOKEN]);
  });

  it("0016 FRS-6: while checking, the button is busy and the field is read only", async () => {
    const answer = deferred<Response>();
    checkEndpoint(() => answer.promise);
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    const button = screen.getByRole("button", { name: "Continue" });
    await expect.element(button).toHaveAttribute("data-pending", "true");
    await expect.element(field).toHaveAttribute("readonly");

    answer.resolve(new Response(null, { status: 204 }));
    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
  });

  it("0016 FRS-6: a rejected token shows the error on the field and keeps it", async () => {
    checkEndpoint(() => problem(401, "setup_token_invalid"));
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    const message =
      "This setup link or token is not valid any more. Make a new one with orvano admin setup-token.";
    await expect.element(field).toHaveAttribute("aria-invalid", "true");
    await expect.element(field).toHaveAccessibleDescription(message);
    await expect.element(field).toHaveValue(TOKEN);
    // The error summary takes focus after a failed submit, and links to the field (spec 0003 FRM-4).
    await expect.element(screen.getByRole("group", { name: /Fix this problem/ })).toHaveFocus();
  });

  it("0016 FRS-6: after a rejection, the same token isn't sent again, and a new one is checked", async () => {
    const sent = checkEndpoint(() =>
      sent.length === 1 ? problem(401, "setup_token_invalid") : new Response(null, { status: 204 }),
    );
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, `${TOKEN}x`);
    await userEvent.keyboard("{Enter}");
    await expect.element(field).toHaveAttribute("aria-invalid", "true");

    // UX-8: pressing Continue on the rejected token shows the error again; the server already refused it.
    await screen.getByRole("button", { name: "Continue" }).click();
    await expect.element(screen.getByRole("group", { name: /Fix this problem/ })).toHaveFocus();
    expect(sent).toEqual([`${TOKEN}x`]);

    await focusElement(field);
    await userEvent.keyboard("{Backspace}");
    await expect.element(field).not.toHaveAttribute("aria-invalid");
    await screen.getByRole("button", { name: "Continue" }).click();

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(sent).toEqual([`${TOKEN}x`, TOKEN]);
  });

  it("0016 FRS-6: a rejected link token stays in the field, with the error", async () => {
    checkEndpoint(() => problem(401, "setup_token_invalid"));
    openWithFragment(`token=${TOKEN}`);

    const screen = await renderWithProviders("/setup", setup);

    const field = screen.getByRole("textbox", { name: "Setup token" });
    await expect.element(field).toHaveAttribute("aria-invalid", "true");
    await expect.element(field).toHaveValue(TOKEN);
    expect(window.location.hash).toBe("");
  });

  it("0016 FRS-6: an empty token is caught before any request", async () => {
    const sent = checkEndpoint();
    const screen = await renderWithProviders("/setup", setup);

    await screen.getByRole("button", { name: "Continue" }).click();

    await expect
      .element(screen.getByRole("textbox", { name: "Setup token" }))
      .toHaveAccessibleDescription("Enter the setup token.");
    expect(sent).toEqual([]);
  });

  it("0016 FRS-6: too many checks say when to try again, from Retry-After, and keep the token", async () => {
    checkEndpoint(() =>
      problem(429, "rate_limited", { retryAfter: 42, headers: { "Retry-After": "42" } }),
    );
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    const summary = screen.getByRole("group", { name: "Fix this problem to continue" });
    await expect.element(summary).toHaveFocus();
    await expect
      .element(summary.getByText("Too many tries. Try again in 42 seconds."))
      .toBeVisible();
    await expect.element(field).toHaveValue(TOKEN);
    await expect.element(field).not.toHaveAttribute("aria-invalid");
  });

  it("0016 FRS-6: a wrong origin says to open the console at its own address", async () => {
    checkEndpoint(() => problem(403, "forbidden"));
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    const summary = screen.getByRole("group", { name: "Fix this problem to continue" });
    await expect
      .element(
        summary.getByText(/^Open the console at its own address, the one in ORVANO_PUBLIC_URL,/),
      )
      .toBeVisible();
  });

  it("0016 FRS-6: a server that can't be reached says so, and Continue checks again", async () => {
    let calls = 0;
    worker.use(
      http.post("/v1/console/setup/token", () => {
        calls += 1;
        return calls === 1 ? Response.error() : new Response(null, { status: 204 });
      }),
    );
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");
    await expect
      .element(screen.getByText("Couldn't reach the server. Check your connection and try again."))
      .toBeVisible();
    await expect.element(field).toHaveValue(TOKEN);

    await screen.getByRole("button", { name: "Continue" }).click();

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(calls).toBe(2);
  });

  it("0016 FRS-6: only spaces count as empty, and nothing is sent", async () => {
    const sent = checkEndpoint();
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, "   ");
    await userEvent.keyboard("{Enter}");

    await expect
      .element(screen.getByRole("textbox", { name: "Setup token" }))
      .toHaveAccessibleDescription("Enter the setup token.");
    expect(sent).toEqual([]);
  });

  it("0016 FRS-6: a body the server refuses shows on the field, as a rejected token does", async () => {
    checkEndpoint(() => problem(400, "validation_failed"));
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect.element(field).toHaveAttribute("aria-invalid", "true");
    await expect
      .element(field)
      .toHaveAccessibleDescription(/^This setup link or token is not valid any more\./);
  });

  it.each([
    [1, "Too many tries. Try again in 1 second."],
    [1.2, "Too many tries. Try again in 2 seconds."],
  ])("0016 FRS-6: a wait of %s seconds reads in whole seconds", async (retryAfter, message) => {
    checkEndpoint(() => problem(429, "rate_limited", { retryAfter }));
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect.element(screen.getByText(message)).toBeVisible();
  });

  it("0016 FRS-6: too many checks with no Retry-After say to wait a moment", async () => {
    checkEndpoint(() => problem(429, "rate_limited"));
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect
      .element(screen.getByText("Too many tries. Wait a moment, then try again."))
      .toBeVisible();
  });

  it.each([
    [500, "internal_error", "The server had a problem. Try again in a moment."],
    [503, "unavailable", "The server had a problem. Try again in a moment."],
    [408, "timeout", "Couldn't reach the server. Check your connection and try again."],
  ])(
    "0016 FRS-6: a %s keeps the token, marks nothing wrong with it, and Continue checks again",
    async (status, code, message) => {
      let calls = 0;
      worker.use(
        http.post("/v1/console/setup/token", () => {
          calls += 1;
          return calls === 1 ? problem(status, code) : new Response(null, { status: 204 });
        }),
      );
      const screen = await renderWithProviders("/setup", setup);

      const field = await typeToken(screen, TOKEN);
      await userEvent.keyboard("{Enter}");
      await expect.element(screen.getByText(message)).toBeVisible();
      await expect.element(field).toHaveValue(TOKEN);
      await expect.element(field).not.toHaveAttribute("aria-invalid");

      await screen.getByRole("button", { name: "Continue" }).click();

      await expect
        .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
        .toBeVisible();
    },
  );

  it("0016 FRS-6: a wrong origin keeps the token for the next try", async () => {
    checkEndpoint(() => problem(403, "forbidden"));
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect
      .element(screen.getByRole("group", { name: "Fix this problem to continue" }))
      .toHaveFocus();
    await expect.element(field).toHaveValue(TOKEN);
    await expect.element(field).not.toHaveAttribute("aria-invalid");
  });

  it("0016 FRS-6: setup completed meanwhile, with the config call failing, ends on the error page", async () => {
    checkEndpoint(() => problem(409, "setup_complete"));
    worker.use(http.get("/v1/console/config", () => Response.error()));
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect.element(screen.getByRole("button", { name: "Reload" })).toBeVisible();
  });

  it("0016 FRS-4: the field then Continue take focus in order, and Enter in the field submits", async () => {
    const sent = checkEndpoint();
    const screen = await renderWithProviders("/setup", setup);

    const field = await typeToken(screen, TOKEN);
    await userEvent.tab();
    await expect.element(screen.getByRole("button", { name: "Continue" })).toHaveFocus();
    await userEvent.tab({ shift: true });
    await expect.element(field).toHaveFocus();
    await userEvent.keyboard("{Enter}");

    await expect
      .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
      .toBeVisible();
    expect(sent).toEqual([TOKEN]);
  });

  it("0016 FRS-6: setup completed meanwhile reloads the config, which sends the page home", async () => {
    checkEndpoint(() => problem(409, "setup_complete"));
    worker.use(
      http.get("/v1/console/config", ({ response }) =>
        response(200).json({ setupRequired: false }),
      ),
    );
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect
      .element(screen.getByRole("heading", { level: 1, name: "Orvano console" }))
      .toBeVisible();
    expect(screen.router.state.location.pathname).toBe("/");
  });
});

/** A right to left locale with no catalog of its own (spec 0015 RTL-1): Lingui keeps the English messages under `ar`. */
async function activateArabic() {
  // The real `ar`, not the pseudo `ar-XB`: CI's WebKit resolves ar-XB as left to right.
  i18n.loadAndActivate({ locale: "ar", messages: sourceMessages });
  document.documentElement.lang = "ar";
  document.documentElement.dir = getDirection("ar");
  await Promise.resolve();
}

async function expectNoAxeViolationsInBothThemes() {
  for (const theme of ["light", "dark"]) {
    document.documentElement.dataset.theme = theme;
    // oxlint-disable-next-line no-await-in-loop -- axe runs one theme at a time
    await expectNoAxeViolations();
  }
}

// Spec 0016 FRS-7, AC-8: axe in both themes, left to right and right to left, for each state with its own markup.
describe("SetupPage accessibility", () => {
  it.each(["ltr", "rtl"] as const)(
    "0016 FRS-7: the token step and its errors pass axe in both themes (%s)",
    async (dir) => {
      if (dir === "rtl") await activateArabic();
      checkEndpoint(() => problem(401, "setup_token_invalid"));
      const screen = await renderWithProviders("/setup", setup);
      await expect.element(screen.getByRole("textbox", { name: "Setup token" })).toBeVisible();
      await expectNoAxeViolationsInBothThemes();

      await typeToken(screen, TOKEN);
      await userEvent.keyboard("{Enter}");
      await expect
        .element(screen.getByRole("textbox", { name: "Setup token" }))
        .toHaveAttribute("aria-invalid", "true");
      await expectNoAxeViolationsInBothThemes();
    },
  );

  it.each(["ltr", "rtl"] as const)(
    "0016 FRS-7: the limited state passes axe in both themes (%s)",
    async (dir) => {
      if (dir === "rtl") await activateArabic();
      checkEndpoint(() => problem(429, "rate_limited", { retryAfter: 5 }));
      const screen = await renderWithProviders("/setup", setup);

      await typeToken(screen, TOKEN);
      await userEvent.keyboard("{Enter}");
      await expect
        .element(screen.getByText("Too many tries. Try again in 5 seconds."))
        .toBeVisible();
      await expectNoAxeViolationsInBothThemes();
    },
  );

  it.each(["ltr", "rtl"] as const)(
    "0016 FRS-7: the accepted state passes axe in both themes (%s)",
    async (dir) => {
      if (dir === "rtl") await activateArabic();
      checkEndpoint();
      openWithFragment(`token=${TOKEN}`);
      const screen = await renderWithProviders("/setup", setup);

      await expect
        .element(screen.getByRole("heading", { level: 2, name: "Setup token accepted" }))
        .toBeVisible();
      await expectNoAxeViolationsInBothThemes();
    },
  );

  it("0016 FRS-7: the wait reads right in Arabic, whose plural categories the catalogue doesn't list", async () => {
    await activateArabic();
    checkEndpoint(() => problem(429, "rate_limited", { retryAfter: 2 }));
    const screen = await renderWithProviders("/setup", setup);

    await typeToken(screen, TOKEN);
    await userEvent.keyboard("{Enter}");

    await expect.element(screen.getByText("Too many tries. Try again in 2 seconds.")).toBeVisible();
  });

  it("0015 RTL-1: in a right to left locale the page lays out from the right", async () => {
    await activateArabic();
    const screen = await renderWithProviders("/setup", setup);
    const field = screen.getByRole("textbox", { name: "Setup token" });
    await expect.element(field).toBeVisible();

    expect(getComputedStyle(field.element()).direction).toBe("rtl");
    const label = screen
      .getByText("Setup token", { exact: true })
      .element()
      .getBoundingClientRect();
    const input = field.element().getBoundingClientRect();
    // The label starts at the inline start, which is the right edge in right to left.
    expect(Math.round(label.right)).toBe(Math.round(input.right));
  });
});
