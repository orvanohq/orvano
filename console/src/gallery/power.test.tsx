import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { DOCS_FALLBACK_URL } from "@/shared/config";
import { paletteRecents } from "@/shared/storage/items";
import { expectNoAxeViolations } from "@/test/axe";
import { notify } from "@/ui";
import { isApplePlatform } from "@/ui/platform";
import { user } from "./fixtures";
import { NARROW, renderScene, resetShellState, setViewport, tabTo, WIDE } from "./render-scene";

// Spec 0015 milestone 5: the command palette, shortcuts, the activity indicator, and the docs pane, through the
// gallery's shell, in Chromium, Firefox, and WebKit.

const project = "/acme/storefront";
const modK = () => (isApplePlatform() ? "{Meta>}k{/Meta}" : "{Control>}k{/Control}");

beforeEach(async () => {
  await setViewport(WIDE);
});

afterEach(() => {
  paletteRecents.remove({ userId: user.id });
  resetShellState();
});

describe("command palette", () => {
  it("0015 AC-13: Mod+K opens a dialog with a combobox, and Escape returns focus", async () => {
    const { screen } = await renderScene(project);
    const storage = screen
      .getByRole("navigation", { name: "Project" })
      .getByRole("link", { name: "Storage" });
    await tabTo(storage.element());

    await userEvent.keyboard(modK());

    const palette = screen.getByRole("dialog", { name: "Command palette" });
    const input = palette.getByRole("combobox", { name: "Search actions" });
    await expect.element(input).toHaveFocus();
    await expect.element(palette.getByRole("menu", { name: "Actions" })).toBeVisible();
    await expectNoAxeViolations();

    await userEvent.keyboard("{Escape}");
    await expect.element(palette).not.toBeInTheDocument();
    await expect.element(storage).toHaveFocus();
  });

  it("0015 AC-13: the palette button shows and declares its shortcut", async () => {
    const { screen } = await renderScene(project);
    const button = screen.getByRole("button", { name: /^Commands/ });

    await expect
      .element(button)
      .toHaveAttribute("aria-keyshortcuts", isApplePlatform() ? "Meta+K" : "Control+K");
    await button.click();
    await expect.element(screen.getByRole("dialog", { name: "Command palette" })).toBeVisible();
  });

  it("0015 AC-13: typing filters, arrows move, Enter runs the action and remembers it", async () => {
    const { router, screen } = await renderScene(project);
    await screen.getByRole("button", { name: /^Commands/ }).click();
    const palette = screen.getByRole("dialog", { name: "Command palette" });
    await expect.element(palette.getByRole("combobox")).toHaveFocus();

    await userEvent.keyboard("stor");
    await expect.element(palette.getByRole("menuitem", { name: "Go to Storage" })).toBeVisible();
    expect(palette.getByRole("menuitem", { name: "Go to Auth" }).elements()).toHaveLength(0);
    await userEvent.keyboard("{ArrowDown}");
    await expect
      .poll(() => palette.getByRole("combobox").element().getAttribute("aria-activedescendant"))
      .toBeTruthy();
    await userEvent.keyboard("{Enter}");

    await expect.poll(() => router.state.location.pathname).toBe("/acme/storefront/storage");
    await expect.element(palette).not.toBeInTheDocument();
    expect(paletteRecents.read({ userId: user.id })).toEqual(["go:storage"]);
  });

  it("0015 AC-13: an empty query shows recents first, and no match says so", async () => {
    paletteRecents.write({ userId: user.id }, ["theme:dark", "go:logs"]);
    const { screen } = await renderScene(project);
    await screen.getByRole("button", { name: /^Commands/ }).click();
    const palette = screen.getByRole("dialog", { name: "Command palette" });

    const recent = palette.getByRole("group", { name: "Recent" });
    await expect.element(recent).toBeVisible();
    const items = recent.getByRole("menuitem").elements();
    expect(items.map((item) => item.textContent)).toEqual(["Use the dark theme", "Go to Logs"]);

    await userEvent.keyboard("zzzz");
    await expect.element(palette.getByText("No matching actions")).toBeVisible();
    expect(palette.getByRole("group", { name: "Recent" }).elements()).toHaveLength(0);
  });

  it("0015 AC-13: lists the scope's nav, theme, density, shortcuts, and help, and switches the theme", async () => {
    const { screen } = await renderScene("/acme");
    await screen.getByRole("button", { name: /^Commands/ }).click();
    const palette = screen.getByRole("dialog", { name: "Command palette" });

    for (const name of [
      "Go to Projects",
      "Go to Members",
      "Go to Organization settings",
      "Use the system theme",
      "Use comfortable density",
      "Use compact density",
      "Show keyboard shortcuts",
      "Open help",
    ]) {
      expect.soft(palette.getByRole("menuitem", { name }).elements()).toHaveLength(1);
    }
    expect(palette.getByRole("menuitem", { name: "Go to Storage" }).elements()).toHaveLength(0);

    await palette.getByRole("menuitem", { name: "Use the dark theme" }).click();
    await expect.poll(() => document.documentElement.dataset.theme).toBe("dark");
  });

  it("0015 AC-13: Mod+K does nothing while a modal is open", async () => {
    await setViewport(NARROW);
    const { screen } = await renderScene(project);
    const open = screen.getByRole("button", { name: "Open navigation" });
    await tabTo(open.element());
    await userEvent.keyboard("{Enter}");
    await expect.element(screen.getByRole("dialog", { name: "Project" })).toBeVisible();

    await userEvent.keyboard(modK());

    // Give a wrongly opened palette a chance to appear (it loads lazily) before checking it didn't.
    await expect.poll(() => document.querySelectorAll("[data-modal]").length).toBe(1);
    expect(screen.getByRole("dialog", { name: "Command palette" }).elements()).toHaveLength(0);
  });
});

describe("keyboard shortcuts", () => {
  it("0015 AC-13: ? opens the shortcut list, which lists the registry", async () => {
    const { screen } = await renderScene(project);
    const storage = screen
      .getByRole("navigation", { name: "Project" })
      .getByRole("link", { name: "Storage" });
    await tabTo(storage.element());

    await userEvent.keyboard("?");

    const help = screen.getByRole("dialog", { name: "Keyboard shortcuts" });
    await expect.element(help.getByText("Open the command palette")).toBeVisible();
    await expect.element(help.getByText("Show keyboard shortcuts")).toBeVisible();
    await expectNoAxeViolations();
    await userEvent.keyboard("{Escape}");
    await expect.element(help).not.toBeInTheDocument();
    await expect.element(storage).toHaveFocus();
  });

  it("0015 AC-13: ? does nothing while you type in a field", async () => {
    const { screen } = await renderScene("/acme/storefront/databases");
    const filter = screen.getByRole("searchbox", { name: "Filter tables" });
    await filter.click();

    await userEvent.keyboard("?");

    await expect.element(filter).toHaveValue("?");
    expect(screen.getByRole("dialog").elements()).toHaveLength(0);
  });

  it("0015 AC-11: the account menu's Keyboard shortcuts opens the same list", async () => {
    const { screen } = await renderScene(project);
    await screen.getByRole("button", { name: "Account" }).click();
    await screen.getByRole("menuitem", { name: "Keyboard shortcuts" }).click();

    await expect.element(screen.getByRole("dialog", { name: "Keyboard shortcuts" })).toBeVisible();
  });
});

describe("activity indicator", () => {
  it("0015 AC-12: shows no running jobs and the message history", async () => {
    const { screen } = await renderScene(project);
    const button = screen.getByRole("button", { name: "Activity", exact: true });
    await button.click();
    const panel = screen.getByRole("dialog", { name: "Activity" });
    await expect.element(panel.getByText("No running jobs")).toBeVisible();
    await expect.element(panel.getByText("No messages yet")).toBeVisible();
    await userEvent.keyboard("{Escape}");
    await expect.element(panel).not.toBeInTheDocument();

    notify({ tone: "success", message: "Table created", source: "Databases" });
    notify({ tone: "error", message: "Deploy failed", source: "Functions" });

    const unread = screen.getByRole("button", { name: "Activity, new messages" });
    await unread.click();
    const items = screen.getByRole("dialog", { name: "Activity" }).getByRole("listitem");
    await expect.poll(() => items.elements().length).toBe(2);
    expect(items.elements()[0]?.textContent).toContain("Deploy failed");
    await expectNoAxeViolations();
    await userEvent.keyboard("{Escape}");
    await expect.element(button).toBeVisible();
  });
});

describe("docs pane", () => {
  it("0015 AC-14: from 64rem it sits beside the content, takes focus, and Escape returns it", async () => {
    const { screen } = await renderScene(project);
    const main = screen.getByRole("main");
    const before = main.element().getBoundingClientRect().width;
    const help = screen.getByRole("button", { name: "Help" });
    await tabTo(help.element());
    await userEvent.keyboard("{Enter}");

    const pane = screen.getByRole("complementary", { name: "Help" });
    await expect.element(pane.getByRole("heading", { name: "Help" })).toHaveFocus();
    await expect.element(help).toHaveAttribute("aria-expanded", "true");
    expect(pane.element().getBoundingClientRect().width).toBe(400);
    expect(main.element().getBoundingClientRect().width).toBe(before - 400);
    const link = pane.getByRole("link", { name: /Open the Orvano docs/ });
    await expect.element(link).toHaveAttribute("href", DOCS_FALLBACK_URL);
    await expect.element(link).toHaveAttribute("target", "_blank");
    await expect.element(link).toHaveAttribute("rel", "noopener noreferrer");
    await expectNoAxeViolations();

    await userEvent.keyboard("{Escape}");
    await expect.element(pane).not.toBeInTheDocument();
    await expect.element(help).toHaveFocus();
    await expect.element(help).toHaveAttribute("aria-expanded", "false");
  });

  it("0015 AC-14: the close button closes it and returns focus to the help button", async () => {
    const { screen } = await renderScene(project);
    const help = screen.getByRole("button", { name: "Help" });
    await help.click();
    await screen.getByRole("button", { name: "Close help" }).click();

    await expect
      .element(screen.getByRole("complementary", { name: "Help" }))
      .not.toBeInTheDocument();
    await expect.element(help).toHaveFocus();
  });

  it("0015 AC-14: below 64rem it is a drawer, and Escape returns focus to the help button", async () => {
    await setViewport(NARROW);
    const { screen } = await renderScene(project);
    const help = screen.getByRole("button", { name: "Help" });
    await tabTo(help.element());
    await userEvent.keyboard("{Enter}");

    const drawer = screen.getByRole("dialog", { name: "Help" });
    await expect.element(drawer.getByRole("heading", { name: "Help" })).toHaveFocus();
    await expectNoAxeViolations();
    await userEvent.keyboard("{Escape}");
    await expect.element(drawer).not.toBeInTheDocument();
    await expect.element(help).toHaveFocus();
  });

  it("0015 AC-13, AC-14: the palette's Open help opens the pane", async () => {
    const { screen } = await renderScene(project);
    await screen.getByRole("button", { name: /^Commands/ }).click();
    await screen.getByRole("menuitem", { name: "Open help" }).click();

    await expect
      .element(screen.getByRole("complementary", { name: "Help" }).getByRole("heading"))
      .toHaveFocus();
  });
});
