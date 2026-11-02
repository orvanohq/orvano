import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { NARROW, renderScene, tabTo, resetShellState, setViewport, WIDE } from "./render-scene";
import { settingsStore } from "./settings";

// Spec 0015 AC-5, AC-7, AC-21: the gallery's scenes, rendered as the gallery renders them (without its toolbar),
// checked in Chromium, Firefox, and WebKit.

const scenes = [
  "/acme/storefront/databases",
  "/acme/storefront/databases/users",
  "/acme/storefront",
  "/acme",
  "/acme/members",
  "/acme/settings",
  "/account/profile",
  "/primitives",
  "/forms",
  "/states",
  "/overlays",
  "/feedback",
  "/data",
  "/boot-error",
  "/root-error",
  "/frame-skeleton",
  "/auth-layout",
];

beforeEach(async () => {
  await setViewport(WIDE);
});

afterEach(() => {
  resetShellState();
  settingsStore.set((state) => ({ ...state, locale: "en", scenario: "normal" }));
});

describe("gallery scenes", () => {
  for (const path of scenes) {
    it(`0015 AC-5: ${path} passes axe in both themes and both densities`, async () => {
      const { screen } = await renderScene(path);
      await expect.element(screen.getByRole("main")).toBeInTheDocument();

      for (const theme of ["light", "dark"]) {
        for (const density of ["comfortable", "compact"]) {
          document.documentElement.dataset.theme = theme;
          document.documentElement.dataset.density = density;
          await expectNoAxeViolations();
        }
      }
    });
  }
});

describe("gallery scenes, narrow", () => {
  for (const path of scenes.slice(0, 2)) {
    it(`0015 AC-5: ${path} passes axe below 64rem`, async () => {
      await setViewport(NARROW);
      const { screen } = await renderScene(path);
      await expect.element(screen.getByRole("main")).toBeInTheDocument();

      await expectNoAxeViolations();
    });
  }
});

describe("AppShell", () => {
  it("0015 AC-7: the skip link is the first focusable element and moves focus to main", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    const skip = screen.getByRole("link", { name: "Skip to content" });

    // First in tab order: nothing focusable comes before it in the document.
    const focusable = document.querySelector(
      "a[href], button, input, select, textarea, [tabindex]:not([tabindex='-1'])",
    );
    expect(focusable).toBe(skip.element());
    // A fresh Firefox frame on CI can take one Tab before focus enters the page.
    await tabTo(skip.element());
    await expect.element(skip).toHaveFocus();
    await expect.element(skip).toBeVisible();
    await userEvent.keyboard("{Enter}");

    await expect.element(screen.getByRole("main")).toHaveFocus();
  });

  it("0015 AC-7: renders one header, the Breadcrumb nav, the scope's nav, and one main", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");

    await expect.element(screen.getByRole("banner")).toBeInTheDocument();
    await expect
      .element(screen.getByRole("navigation", { name: "Breadcrumb" }))
      .toBeInTheDocument();
    await expect.element(screen.getByRole("navigation", { name: "Project" })).toBeInTheDocument();
    expect(screen.getByRole("main").elements()).toHaveLength(1);
    expect(screen.getByRole("banner").elements()).toHaveLength(1);
  });

  it("0015 AC-7: marks the current service, matching Overview exactly", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    const sidebar = screen.getByRole("navigation", { name: "Project" });

    await expect
      .element(sidebar.getByRole("link", { name: "Databases" }))
      .toHaveAttribute("aria-current", "page");
    await expect
      .element(sidebar.getByRole("link", { name: "Overview" }))
      .not.toHaveAttribute("aria-current");
    expect(document.querySelectorAll("[aria-current='page']")).toHaveLength(2); // the item and the last crumb
  });

  it("0015 AC-7: Overview is current on the project home, and the sidebar navigates by keyboard", async () => {
    const { router, screen } = await renderScene("/acme/storefront");
    const sidebar = screen.getByRole("navigation", { name: "Project" });
    await expect
      .element(sidebar.getByRole("link", { name: "Overview" }))
      .toHaveAttribute("aria-current", "page");

    // By keyboard: past the skip link, the top bar, and Overview.
    await tabTo(sidebar.getByRole("link", { name: "Auth" }).element());
    await expect.element(sidebar.getByRole("link", { name: "Auth" })).toHaveFocus();
    await userEvent.keyboard("{Enter}");

    await expect
      .element(sidebar.getByRole("link", { name: "Auth" }))
      .toHaveAttribute("aria-current", "page");
    expect(router.state.location.pathname).toBe("/acme/storefront/auth");
  });

  it("0015 AC-7: labels the sidebar by scope", async () => {
    const org = await renderScene("/acme");
    await expect
      .element(org.screen.getByRole("navigation", { name: "Organization" }))
      .toBeInTheDocument();
    await org.screen.unmount();

    const account = await renderScene("/account/security");
    await expect
      .element(account.screen.getByRole("navigation", { name: "Account" }))
      .toBeInTheDocument();
    await expect
      .element(account.screen.getByRole("link", { name: "Security" }))
      .toHaveAttribute("aria-current", "page");
  });

  it("0015 AC-7: the brand link is named Orvano home", async () => {
    const { screen } = await renderScene("/acme/storefront");

    await expect.element(screen.getByRole("link", { name: "Orvano home" })).toBeInTheDocument();
  });
});

describe("AuthLayout", () => {
  it("0015 AC-21: renders the brand link and a main card, with no session", async () => {
    const { screen } = await renderScene("/auth-layout");

    await expect.element(screen.getByRole("link", { name: "Orvano home" })).toBeInTheDocument();
    await expect
      .element(screen.getByRole("main").getByRole("heading", { level: 1 }))
      .toHaveTextContent("Sign in to Orvano");
  });
});

describe("AuthLayout's sign in form", () => {
  it("0015 AC-21: has email and password fields and a large Sign in button", async () => {
    const { screen } = await renderScene("/auth-layout");
    const main = screen.getByRole("main");

    await expect.element(main.getByRole("textbox", { name: "Email address" })).toBeVisible();
    await expect.element(main.getByLabelText("Password", { exact: true })).toBeVisible();
    const button = main.getByRole("button", { name: "Sign in" });
    expect(button.element().getBoundingClientRect().height).toBeGreaterThanOrEqual(40);
    expect(document.body.textContent).not.toContain("milestone");
  });

  it("0015 AC-21: a failed request shows an InlineError with its request ID", async () => {
    settingsStore.set((state) => ({ ...state, scenario: "fail" }));
    const { screen } = await renderScene("/auth-layout");
    const main = screen.getByRole("main");
    await main.getByRole("textbox", { name: "Email address" }).fill("ada@example.com");
    await main.getByLabelText("Password", { exact: true }).fill("correct horse");

    await main.getByRole("button", { name: "Sign in" }).click();

    await expect.element(main.getByText("Couldn't sign you in")).toBeVisible();
    await expect.element(main.getByText("req_01JB8Z4Q7W2N")).toBeVisible();
    await expectNoAxeViolations();
  });
});

describe("gallery scenes, their own titles and structure", () => {
  it("0015 GAL-2: each scene sets its own document title", async () => {
    const forms = await renderScene("/forms");
    await expect.element(forms.screen.getByRole("main")).toBeInTheDocument();
    await expect.poll(() => document.title).toBe("Forms · Gallery · Orvano");
    await forms.screen.unmount();

    const boot = await renderScene("/boot-error");
    await expect.poll(() => document.title).toBe("Boot error · Gallery · Orvano");
    await boot.screen.unmount();
  });

  it("0015 GAL-2: demo labels are not headings, so a card's own title is the h2", async () => {
    const { screen } = await renderScene("/data");
    const main = screen.getByRole("main");

    await expect
      .element(main.getByRole("region", { name: "SettingsCard and DangerZone" }))
      .toBeInTheDocument();
    expect(main.getByRole("heading", { name: "Table, sortable" }).elements()).toHaveLength(0);
    await expect.element(main.getByRole("heading", { level: 2, name: "General" })).toBeVisible();
  });

  it("0015 GAL-2: banners each have their own message, with and without an action", async () => {
    const { screen } = await renderScene("/feedback");
    const plain = screen.getByRole("region", { name: "Banner", exact: true });
    const withAction = screen.getByRole("region", { name: "Banner, with an action" });

    await expect.element(plain.getByText("Backup finished")).toBeVisible();
    await expect.element(plain.getByText("Storage is full")).toBeVisible();
    expect(plain.getByRole("button").elements()).toHaveLength(0);
    expect(withAction.getByRole("button").elements()).toHaveLength(5);
  });
});

describe("SettingsCard in the data scene", () => {
  it("0015 GAL-2: Update is unavailable until the name changes (FRM-5, the caller's job)", async () => {
    const { screen } = await renderScene("/data");
    const card = screen.getByRole("region", { name: "General" });
    const update = card.getByRole("button", { name: "Update" });

    await expect.element(update).toHaveAttribute("aria-disabled", "true");
    await card.getByRole("textbox", { name: "Project name" }).fill("Storefront EU");

    await expect.element(update).not.toHaveAttribute("aria-disabled");
  });
});

describe("the field States scene", () => {
  it("0015 GAL-2: shows invalid, read only, and disabled fields without a submit", async () => {
    const { screen } = await renderScene("/states");
    const invalid = screen.getByRole("region", { name: "Invalid" });
    const readOnly = screen.getByRole("region", { name: "Read only" });
    const disabled = screen.getByRole("region", { name: "Disabled" });

    await expect
      .element(invalid.getByRole("textbox", { name: "Bucket name" }))
      .toHaveAttribute("aria-invalid", "true");
    await expect.element(invalid.getByText("Use at least 8 characters.")).toBeVisible();
    await expect.element(invalid.getByText("Choose a region.")).toBeVisible();
    await expect
      .element(readOnly.getByRole("textbox", { name: "Bucket name" }))
      .toHaveAttribute("readonly");
    await expect.element(disabled.getByRole("textbox", { name: "Bucket name" })).toBeDisabled();
    expect(document.activeElement).toBe(document.body);
  });

  it("0015 GAL-2: help text shows only in its own set", async () => {
    const { screen } = await renderScene("/states");

    await expect
      .element(
        screen
          .getByRole("region", { name: "With help text" })
          .getByRole("textbox", { name: "Bucket name" }),
      )
      .toHaveAccessibleDescription("Lowercase, unique in the project.");
  });
});

describe("the fixture scenario", () => {
  it("0015 GAL-2: Empty shows the resource list's empty state", async () => {
    settingsStore.set((state) => ({ ...state, scenario: "empty" }));
    const { screen } = await renderScene("/acme/storefront/databases");

    await expect.element(screen.getByText("No tables yet")).toBeVisible();
  });

  it("0015 GAL-2: Slow shows the resource list loading", async () => {
    settingsStore.set((state) => ({ ...state, scenario: "slow" }));
    const { screen } = await renderScene("/acme/storefront/databases");
    await expect.element(screen.getByRole("main")).toBeInTheDocument();

    expect(screen.getByRole("link", { name: "orders" }).elements()).toHaveLength(0);
    await expectNoAxeViolations();
  });

  it("0015 GAL-2: Fail makes the project switcher's search fail", async () => {
    settingsStore.set((state) => ({ ...state, scenario: "fail" }));
    const { screen } = await renderScene("/acme/storefront");

    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();

    await expect.element(screen.getByRole("button", { name: "Retry" })).toBeVisible();
  });
});

describe("the pseudo locale on scene strings", () => {
  it("0015 GAL-2: scene text is pseudo localized and longer, user data is not", async () => {
    settingsStore.set((state) => ({ ...state, locale: "pseudo-LOCALE" }));
    const { screen } = await renderScene("/data");
    const heading = screen.getByRole("heading", { level: 1 });

    await expect.element(heading).not.toHaveTextContent("Data display");
    expect(heading.element().textContent.length).toBeGreaterThanOrEqual(
      "Data display".length * 1.3,
    );
    await expect.element(screen.getByText("ada@example.com")).toBeVisible();
  });
});
