import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { NARROW, renderScene, tabTo, resetShellState, setViewport, WIDE } from "./render-scene";

// Spec 0015 AC-5, AC-7, AC-21: the gallery's scenes, rendered as the gallery renders them (without its toolbar),
// checked in Chromium, Firefox, and WebKit.

const scenes = [
  "/acme/storefront/databases",
  "/acme/storefront/databases/users",
  "/acme/storefront",
  "/acme",
  "/account/profile",
  "/primitives",
  "/forms",
  "/overlays",
  "/auth-layout",
];

beforeEach(async () => {
  await setViewport(WIDE);
});

afterEach(resetShellState);

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
