import { afterEach, describe, expect, it, vi } from "vitest";
import { paletteRecents } from "@/shared/storage/items";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { SearchIcon } from "../icons";
import type { PaletteAction } from "../shell/palette-actions";
import { CommandPalette } from "./index";

// Spec 0015 AC-13, SHL-10: the palette's groups with an empty query, and its motion across a theme switch, in three
// engines. The shell's own palette flows are in `src/gallery/power.test.tsx`.

const userId = "usr_palette";

const action = (id: string, label: string, section: PaletteAction["section"]): PaletteAction => ({
  id,
  section,
  label: () => label,
  icon: SearchIcon,
  run: { kind: "call", call: vi.fn<() => void>() },
});

const actions = [
  action("go:databases", "Go to Databases", "navigation"),
  action("go:storage", "Go to Storage", "navigation"),
  action("theme:dark", "Use the dark theme", "theme"),
];

afterEach(() => {
  paletteRecents.remove({ userId });
  delete document.documentElement.dataset.theme;
});

describe("CommandPalette", () => {
  it("0015 AC-13: with an empty query a recent action shows under Recent only (G-20)", async () => {
    paletteRecents.write({ userId }, ["go:databases"]);
    const { screen } = await renderInRouter(
      <CommandPalette isOpen onOpenChange={() => undefined} actions={actions} userId={userId} />,
    );
    const palette = screen.getByRole("dialog", { name: "Command palette" });

    const recent = palette.getByRole("group", { name: "Recent" });
    await expect.element(recent).toBeVisible();
    expect(
      recent
        .getByRole("menuitem")
        .elements()
        .map((item) => item.textContent),
    ).toEqual(["Go to Databases"]);
    expect(palette.getByRole("menuitem", { name: "Go to Databases" }).elements()).toHaveLength(1);
    const goTo = palette.getByRole("group", { name: "Go to" });
    expect(
      goTo
        .getByRole("menuitem")
        .elements()
        .map((item) => item.textContent),
    ).toEqual(["Go to Storage"]);
    await expectNoAxeViolations();
  });

  it("0015 AC-13: a query lists a recent action in its own group again", async () => {
    paletteRecents.write({ userId }, ["go:databases"]);
    const { screen } = await renderInRouter(
      <CommandPalette isOpen onOpenChange={() => undefined} actions={actions} userId={userId} />,
    );
    const palette = screen.getByRole("dialog", { name: "Command palette" });

    await palette.getByRole("combobox").fill("data");
    await expect
      .element(palette.getByRole("group", { name: "Go to" }).getByRole("menuitem"))
      .toHaveTextContent("Go to Databases");
    expect(palette.getByRole("group", { name: "Recent" }).elements()).toHaveLength(0);
  });

  // THM-5: a theme switch never animates colors (R-25).
  it("0015 THM-5: the palette animates only opacity and scale across a theme switch", async () => {
    const { screen } = await renderInRouter(
      <CommandPalette isOpen onOpenChange={() => undefined} actions={actions} userId={userId} />,
    );
    const palette = screen.getByRole("dialog", { name: "Command palette" });
    await expect.element(palette).toBeVisible();

    document.documentElement.dataset.theme = "dark";
    expect(getComputedStyle(palette.element().parentElement!).transitionProperty).toBe(
      "opacity, scale",
    );
  });
});
