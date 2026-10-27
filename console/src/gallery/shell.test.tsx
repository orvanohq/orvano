import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { layout, resourceListLayout, themePreference } from "@/shared/storage/items";
import { expectNoAxeViolations } from "@/test/axe";
import { user } from "./fixtures";
import {
  NARROW,
  renderScene,
  resetShellState,
  setViewport,
  tabTo,
  WIDE,
  focusElement,
} from "./render-scene";

// Spec 0015 milestone 2: the shell through the gallery's scenes, in Chromium, Firefox, and WebKit. The switchers'
// timing is in src/ui/shell/switcher.test.tsx.

const table = "/acme/storefront/databases/users";
const ids = { userId: user.id, projectId: "storefront", service: "databases" };

beforeEach(async () => {
  await setViewport(WIDE);
});

afterEach(resetShellState);

describe("primary sidebar", () => {
  it("0015 AC-8: collapses to the icon rail, keeps names, and remembers it", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    const sidebar = screen.getByRole("navigation", { name: "Project" });

    await sidebar.getByRole("button", { name: "Collapse sidebar" }).click();

    await expect.element(sidebar.getByRole("button", { name: "Expand sidebar" })).toBeVisible();
    expect(sidebar.element().getBoundingClientRect().width).toBe(56);
    await expect
      .element(sidebar.getByRole("link", { name: "Databases" }))
      .toHaveAttribute("aria-current", "page");
    expect(layout.read({ userId: user.id }).sidebarCollapsed).toBe(true);
    await expectNoAxeViolations();

    await screen.unmount();
    const again = await renderScene("/acme/storefront");
    await expect
      .element(
        again.screen
          .getByRole("navigation", { name: "Project" })
          .getByRole("button", { name: "Expand sidebar" }),
      )
      .toBeVisible();
  });

  it("0015 AC-8: a rail item shows its name in a tooltip on keyboard focus", async () => {
    layout.write({ userId: user.id }, { sidebarCollapsed: true, density: "comfortable" });
    const { screen } = await renderScene("/acme/storefront");
    const storage = screen
      .getByRole("navigation", { name: "Project" })
      .getByRole("link", { name: "Storage" });

    await tabTo(storage.element());

    await expect.element(storage).toHaveFocus();
    await expect.element(screen.getByRole("tooltip")).toHaveTextContent("Storage");
  });
});

describe("narrow layout", () => {
  beforeEach(async () => {
    await setViewport(NARROW);
  });

  it("0015 AC-8: the sidebar is a labeled drawer that traps focus and returns it on Escape", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    const open = screen.getByRole("button", { name: "Open navigation" });
    expect(screen.getByRole("navigation", { name: "Project" }).elements()).toHaveLength(0);

    await tabTo(open.element());
    await userEvent.keyboard("{Enter}");

    const drawer = screen.getByRole("dialog", { name: "Project" });
    await expect.element(drawer).toBeVisible();
    for (let index = 0; index < 15; index++) {
      await userEvent.tab();
      expect(drawer.element().contains(document.activeElement)).toBe(true);
    }
    await expectNoAxeViolations();

    await userEvent.keyboard("{Escape}");
    await expect.element(drawer).not.toBeInTheDocument();
    await expect.element(open).toHaveFocus();
  });

  it("0015 AC-8: navigating from the drawer closes it", async () => {
    const { router, screen } = await renderScene("/acme/storefront/databases");
    await screen.getByRole("button", { name: "Open navigation" }).click();

    await screen
      .getByRole("dialog", { name: "Project" })
      .getByRole("link", { name: "Storage" })
      .click();

    await expect.poll(() => router.state.location.pathname).toBe("/acme/storefront/storage");
    await expect.element(screen.getByRole("dialog")).not.toBeInTheDocument();
  });

  it("0015 AC-8: an open drawer closes when the window widens past 64rem", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    await screen.getByRole("button", { name: "Open navigation" }).click();
    await expect.element(screen.getByRole("dialog", { name: "Project" })).toBeVisible();

    await setViewport(WIDE);

    await expect.element(screen.getByRole("dialog")).not.toBeInTheDocument();
    await expect.element(screen.getByRole("navigation", { name: "Project" })).toBeVisible();
  });

  it("0015 AC-8: the resource list is a drawer opened by Show list", async () => {
    const { screen } = await renderScene(table);
    const show = screen.getByRole("button", { name: "Show list" });

    // By keyboard: a mouse press leaves focus outside the drawer in Firefox, so Escape would go elsewhere.
    await tabTo(show.element());
    await userEvent.keyboard("{Enter}");

    const drawer = screen.getByRole("dialog", { name: "Tables" });
    await expect
      .element(drawer.getByRole("link", { name: "users" }))
      .toHaveAttribute("aria-current", "page");
    await expect.poll(() => drawer.element().contains(document.activeElement)).toBe(true);
    await userEvent.keyboard("{Escape}");
    await expect.element(drawer).not.toBeInTheDocument();
    await expect.element(show).toHaveFocus();
  });

  it("0015 AC-9: below 64rem the middle crumbs collapse into a menu", async () => {
    const { router, screen } = await renderScene(table);
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });
    await expect
      .element(breadcrumb.getByRole("button", { name: "Switch organization: Acme" }))
      .toBeVisible();
    expect(
      breadcrumb.getByRole("button", { name: "Switch project: Storefront" }).elements(),
    ).toHaveLength(0);

    await breadcrumb.getByRole("button", { name: "Show the full path" }).click();
    const menu = screen.getByRole("menu");
    await expect.element(menu.getByRole("menuitem", { name: "Storefront" })).toBeVisible();
    await menu.getByRole("menuitem", { name: "Databases" }).click();

    await expect.poll(() => router.state.location.pathname).toBe("/acme/storefront/databases");
  });

  it("0015 AC-9: the project item in the collapsed menu opens the project switcher", async () => {
    const { screen } = await renderScene(table);

    await screen.getByRole("button", { name: "Show the full path" }).click();
    await screen.getByRole("menuitem", { name: "Storefront" }).click();

    await expect.element(screen.getByRole("dialog", { name: "Switch project" })).toBeVisible();
    await expect
      .element(
        screen.getByRole("listbox", { name: "Projects" }).getByRole("option", { name: "Billing" }),
      )
      .toBeVisible();
  });

  it("0015 AC-8: at 320 px wide no shell region scrolls sideways", async () => {
    await setViewport({ width: 320, height: 640 });
    const { screen } = await renderScene(table);
    await expect.element(screen.getByRole("main")).toBeVisible();

    const root = document.documentElement;
    expect(root.scrollWidth).toBeLessThanOrEqual(root.clientWidth);
    for (const region of ["banner", "main"] as const) {
      const element = screen.getByRole(region).element();
      expect.soft(element.scrollWidth).toBeLessThanOrEqual(element.clientWidth);
    }
  });
});

describe("breadcrumbs and document title", () => {
  it("0015 AC-9: renders the crumbs, the last one current, with the switchers named from the model", async () => {
    const { screen } = await renderScene(table);
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });

    await expect
      .element(breadcrumb.getByRole("button", { name: "Switch organization: Acme" }))
      .toBeVisible();
    await expect
      .element(breadcrumb.getByRole("button", { name: "Switch project: Storefront" }))
      .toBeVisible();
    await expect.element(breadcrumb.getByRole("link", { name: "Databases" })).toBeVisible();
    await expect.element(breadcrumb.getByText("users")).toHaveAttribute("aria-current", "page");
    expect(breadcrumb.getByRole("list").elements()).toHaveLength(1);
  });

  it("0015 AC-9: on a nested route only the last crumb is current", async () => {
    const { screen } = await renderScene(table);
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });

    await expect.element(breadcrumb.getByText("users")).toHaveAttribute("aria-current", "page");
    // The router marks a link to an ancestor route active; a crumb must not repeat that (SRT-4).
    await expect
      .element(breadcrumb.getByRole("link", { name: "Databases" }))
      .not.toHaveAttribute("aria-current");
    expect(breadcrumb.element().querySelectorAll("[aria-current]")).toHaveLength(1);
  });

  it("0015 AC-9: the document title mirrors the crumbs (UX-4)", async () => {
    await renderScene(table);

    await expect.poll(() => document.title).toBe("users · Databases · Storefront · Orvano");
  });
});

const separator = (screen: Awaited<ReturnType<typeof renderScene>>["screen"]) =>
  screen.getByRole("separator", { name: "Resize table list" });

describe("resource layout", () => {
  it("0015 AC-10: keeps the list beside the open resource, the open one current", async () => {
    const { router, screen } = await renderScene(table);
    const list = screen.getByRole("navigation", { name: "Tables" });
    const before = list.element();

    await list.getByRole("link", { name: "orders" }).click();

    await expect
      .poll(() => router.state.location.pathname)
      .toBe("/acme/storefront/databases/orders");
    expect(list.element()).toBe(before); // the same element: the list stayed mounted
    await expect
      .element(list.getByRole("link", { name: "orders" }))
      .toHaveAttribute("aria-current", "page");
    await expect
      .element(list.getByRole("link", { name: "users" }))
      .not.toHaveAttribute("aria-current");
  });

  it("0015 AC-10: arrow keys move the separator 16 px, Home and End jump to the limits, and each key saves", async () => {
    const { screen } = await renderScene(table);
    const handle = separator(screen);
    await expect.element(handle).toHaveAttribute("aria-valuenow", "280");

    // Focused directly: the test is about the keys, and WebKit's Tab order past many links varies by settings.
    await expect.element(handle).toHaveAttribute("aria-valuemax");
    await focusElement(handle);
    await userEvent.keyboard("{ArrowRight}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "296");
    expect(resourceListLayout.read(ids).width).toBe(296);

    await userEvent.keyboard("{ArrowLeft}{ArrowLeft}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "264");

    await userEvent.keyboard("{Home}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "200");
    await userEvent.keyboard("{End}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "480");
    expect(resourceListLayout.read(ids).width).toBe(480);
  });

  it("0015 AC-10: the maximum leaves the content at least 480 px", async () => {
    // 1100 px less the 240 px sidebar leaves 860 px, so the list may take 380.
    await setViewport({ width: 1100, height: 800 });
    const { screen } = await renderScene(table);
    const handle = separator(screen);

    await expect.element(handle).toHaveAttribute("aria-valuemax", "380");
    // Focused directly: the test is about the keys, and WebKit's Tab order past many links varies by settings.
    await expect.element(handle).toHaveAttribute("aria-valuemax");
    await focusElement(handle);
    await userEvent.keyboard("{End}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "380");
    expect(screen.getByRole("main").element().getBoundingClientRect().width).toBeGreaterThanOrEqual(
      480,
    );
  });

  it("0015 AC-10: the width survives a remount, and an out of range width resets to 280", async () => {
    resourceListLayout.write(ids, { collapsed: false, width: 344 });
    const first = await renderScene(table);
    await expect.element(separator(first.screen)).toHaveAttribute("aria-valuenow", "344");
    expect(
      first.screen
        .getByRole("navigation", { name: "Tables" })
        .element()
        .parentElement?.parentElement?.getBoundingClientRect().width,
    ).toBe(344);
    await first.screen.unmount();

    resourceListLayout.write(ids, { collapsed: false, width: 900 }); // out of range, as an old or edited entry
    const second = await renderScene(table);
    await expect.element(separator(second.screen)).toHaveAttribute("aria-valuenow", "280");
  });

  it("0015 AC-10: a pointer drag resizes the list and saves when it ends", async () => {
    const { screen } = await renderScene(table);
    const handle = separator(screen).element();
    const box = handle.getBoundingClientRect();
    const x = box.left + box.width / 2;
    const y = box.top + box.height / 2;

    handle.dispatchEvent(
      new PointerEvent("pointerdown", {
        bubbles: true,
        clientX: x,
        clientY: y,
        pointerId: 1,
        isPrimary: true,
        button: 0,
        pointerType: "mouse",
      }),
    );
    window.dispatchEvent(
      new PointerEvent("pointermove", {
        bubbles: true,
        clientX: x + 40,
        clientY: y,
        pointerId: 1,
        isPrimary: true,
        pointerType: "mouse",
      }),
    );
    await expect.element(separator(screen)).toHaveAttribute("aria-valuenow", "320");
    expect(resourceListLayout.read(ids).width).toBe(280); // not saved during the drag
    window.dispatchEvent(
      new PointerEvent("pointerup", {
        bubbles: true,
        clientX: x + 40,
        clientY: y,
        pointerId: 1,
        isPrimary: true,
        button: 0,
        pointerType: "mouse",
      }),
    );

    await expect.poll(() => resourceListLayout.read(ids).width).toBe(320);
  });

  it("0015 AC-10: collapses to a 40 px strip with an expand button, and remembers it", async () => {
    const { screen } = await renderScene(table);

    await screen.getByRole("button", { name: "Collapse list" }).click();

    const expand = screen.getByRole("button", { name: "Expand list" });
    await expect.element(expand).toBeVisible();
    expect(expand.element().parentElement?.getBoundingClientRect().width).toBe(40);
    expect(resourceListLayout.read(ids).collapsed).toBe(true);
    await expectNoAxeViolations();

    await expand.click();
    await expect.element(screen.getByRole("navigation", { name: "Tables" })).toBeVisible();
  });

  it("0015 AC-10: filters the list, keeping full names as accessible names", async () => {
    const { screen } = await renderScene(table);
    const list = screen.getByRole("navigation", { name: "Tables" });
    const long = "audit_log_entries_with_a_very_long_name_that_truncates";
    await expect.element(list.getByRole("link", { name: long })).toBeVisible();

    await list.getByRole("searchbox", { name: "Filter tables" }).fill("pay");

    await expect.element(list.getByRole("link", { name: "payments" })).toBeVisible();
    expect(list.getByRole("link").elements()).toHaveLength(1);
    await list.getByRole("searchbox", { name: "Filter tables" }).fill("zzz");
    await expect.element(list.getByText("Nothing matches “zzz”")).toBeVisible();
  });
});

describe("page header and route tabs", () => {
  it("0015 AC-15: the page has one h1 and route tabs mark the current tab", async () => {
    const { router, screen } = await renderScene(table);
    const tabs = screen.getByRole("navigation", { name: "Table" });

    await expect
      .element(screen.getByRole("heading", { level: 1, name: "users" }))
      .toHaveAttribute("tabindex", "-1");
    await expect
      .element(tabs.getByRole("link", { name: "Rows" }))
      .toHaveAttribute("aria-current", "page");

    await tabTo(tabs.getByRole("link", { name: "Columns" }).element());
    await userEvent.keyboard("{Enter}");

    await expect.poll(() => router.state.location.pathname).toBe(`${table}/columns`);
    await expect
      .element(tabs.getByRole("link", { name: "Columns" }))
      .toHaveAttribute("aria-current", "page");
    await expect
      .element(tabs.getByRole("link", { name: "Rows" }))
      .not.toHaveAttribute("aria-current");
    expect(router.state.location.state).toMatchObject({ keepFocus: true });
  });
});

describe("account menu", () => {
  it("0015 AC-11: shows the user, and Theme and Density as checked groups", async () => {
    const { screen } = await renderScene("/acme/storefront");
    const button = screen.getByRole("button", { name: "Account" });

    await tabTo(button.element());
    await userEvent.keyboard("{Enter}");

    const menu = screen.getByRole("menu", { name: "Account" });
    await expect.element(screen.getByText("ada@example.com")).toBeVisible();
    await expect.element(screen.getByText("Ada Lovelace")).toBeVisible();
    await expect.element(menu.getByRole("group", { name: "Theme" })).toBeVisible();
    await expect
      .element(menu.getByRole("menuitemradio", { name: "System" }))
      .toHaveAttribute("aria-checked", "true");
    await expect
      .element(menu.getByRole("menuitemradio", { name: "Comfortable" }))
      .toHaveAttribute("aria-checked", "true");
    await expect.element(menu.getByRole("menuitem", { name: "Keyboard shortcuts" })).toBeVisible();
    await expectNoAxeViolations();

    await userEvent.keyboard("{Escape}");
    await expect.element(menu).not.toBeInTheDocument();
    await expect.element(button).toHaveFocus();
  });

  it("0015 AC-11: choosing Dark switches the theme at once", async () => {
    const { screen } = await renderScene("/acme/storefront");

    await screen.getByRole("button", { name: "Account" }).click();
    await screen.getByRole("menuitemradio", { name: "Dark" }).click();

    expect(document.documentElement.dataset.theme).toBe("dark");
    expect(themePreference.read(undefined)).toBe("dark");
  });

  it("0015 AC-11: Compact sets data-density, 28 px rows, and saves it; forms and the sidebar keep their size", async () => {
    const { screen } = await renderScene(table);
    const row = screen
      .getByRole("navigation", { name: "Tables" })
      .getByRole("link", { name: "users" });
    const sidebarItem = screen
      .getByRole("navigation", { name: "Project" })
      .getByRole("link", { name: "Databases" });
    expect(row.element().getBoundingClientRect().height).toBe(40);

    await screen.getByRole("button", { name: "Account" }).click();
    await screen.getByRole("menuitemradio", { name: "Compact" }).click();

    expect(document.documentElement.dataset.density).toBe("compact");
    expect(layout.read({ userId: user.id }).density).toBe("compact");
    expect(row.element().getBoundingClientRect().height).toBe(28);
    expect(sidebarItem.element().getBoundingClientRect().height).toBe(32);
    // UX-32: every target stays at least 24 by 24 px.
    for (const target of document.querySelectorAll("button, a")) {
      const box = target.getBoundingClientRect();
      // Visually hidden: the skip link until focused, and React Aria's 1 px dismiss buttons for screen readers.
      if (target.closest(".sr-only") || box.width <= 1) continue;
      expect.soft(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(24);
    }
  });

  it("0015 AC-11: Sign out calls the model's onSignOut", async () => {
    const { screen } = await renderScene("/acme/storefront");
    let signedOut = false;
    window.addEventListener("gallery:sign-out", () => (signedOut = true), { once: true });

    await screen.getByRole("button", { name: "Account" }).click();
    await screen.getByRole("menuitem", { name: "Sign out" }).click();

    expect(signedOut).toBe(true);
  });
});
