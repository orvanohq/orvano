import type { ReactNode } from "react";
import { I18nProvider } from "@lingui/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "vitest-browser-react";
import { expectNoAxeViolations } from "@/test/axe";
import { activateLocale, i18n } from "@/shared/i18n";
import { BootErrorPage, RootErrorPage } from "./error-page";

// Spec 0015 AC-21, spec 0003 FLT-2: the plain error pages use the tokens and both themes, render with no router,
// session, or query client, and pass axe. In three engines.

afterEach(() => {
  vi.restoreAllMocks();
  delete document.documentElement.dataset.theme;
});

/** Renders the way main.tsx renders the boot error page: only the catalog, nothing else. */
async function renderBare(element: ReactNode) {
  if (!i18n.locale) await activateLocale("en");
  return render(<I18nProvider i18n={i18n}>{element}</I18nProvider>);
}

describe("error pages", () => {
  it("0015 AC-21: the boot error page renders before anything else exists and passes axe in both themes", async () => {
    const report = vi.spyOn(console, "error").mockImplementation(() => undefined);
    const screen = await renderBare(
      <BootErrorPage error={{ kind: "server", requestId: "req_01JB8Z" }} />,
    );

    await expect
      .element(screen.getByRole("heading", { level: 1, name: "Orvano couldn't start" }))
      .toBeVisible();
    await expect
      .element(screen.getByRole("textbox", { name: "Request ID" }))
      .toHaveValue("req_01JB8Z");
    await expect.element(screen.getByRole("button", { name: "Reload" })).toBeVisible();
    expect(report).toHaveBeenCalled(); // FLT-4: the one reporter
    for (const theme of ["light", "dark"]) {
      document.documentElement.dataset.theme = theme;
      // oxlint-disable-next-line no-await-in-loop -- axe runs one theme at a time
      await expectNoAxeViolations();
    }
  });

  it("0015 AC-21: the root error page passes axe in both themes, without a request ID for a plain error", async () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const screen = await renderBare(<RootErrorPage error={new Error("render failed")} />);

    await expect
      .element(screen.getByRole("heading", { level: 1, name: "Something went wrong" }))
      .toBeVisible();
    expect(screen.getByRole("textbox", { name: "Request ID" }).elements()).toHaveLength(0);
    for (const theme of ["light", "dark"]) {
      document.documentElement.dataset.theme = theme;
      // oxlint-disable-next-line no-await-in-loop -- axe runs one theme at a time
      await expectNoAxeViolations();
    }
  });
});
