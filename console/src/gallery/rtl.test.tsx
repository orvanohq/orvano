import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { emulateForcedColors } from "@/test/media";
import {
  NARROW,
  renderScene,
  resetShellState,
  setViewport,
  WIDE,
  focusElement,
} from "./render-scene";

// Spec 0015 AC-18, RTL-1: the project shell with dir="rtl" and React Aria's locale ar-XB, in three engines.

const table = "/acme/storefront/databases/users";

beforeEach(async () => {
  await setViewport(WIDE);
});

afterEach(resetShellState);

describe("right to left", () => {
  it("0015 AC-18: the sidebar sits at the inline start, the right edge", async () => {
    const { screen } = await renderScene(table, { direction: "rtl" });
    const sidebar = screen
      .getByRole("navigation", { name: "Project" })
      .element()
      .getBoundingClientRect();
    const list = screen
      .getByRole("navigation", { name: "Tables" })
      .element()
      .getBoundingClientRect();
    const main = screen.getByRole("main").element().getBoundingClientRect();

    expect(sidebar.right).toBe(document.documentElement.clientWidth);
    expect(list.right).toBeLessThanOrEqual(sidebar.left);
    expect(main.right).toBeLessThanOrEqual(list.left);
    await expectNoAxeViolations();
  });

  it("0015 AC-18: the separator's arrow keys are mirrored", async () => {
    const { screen } = await renderScene(table, { direction: "rtl" });
    const handle = screen.getByRole("separator", { name: "Resize table list" });

    // Focused directly: the test is about the keys, and WebKit's Tab order past many links varies by settings.
    await expect.element(handle).toHaveAttribute("aria-valuemax");
    await focusElement(handle);
    // The content is to the left, so ArrowLeft grows the list.
    await userEvent.keyboard("{ArrowLeft}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "296");
    await userEvent.keyboard("{ArrowRight}{ArrowRight}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "264");
  });

  it("0015 AC-18: direction icons are flipped", async () => {
    const { screen } = await renderScene(table, { direction: "rtl" });
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" }).element();
    const chevron = breadcrumb.querySelector("svg.lucide-chevron-right");
    const collapse = screen
      .getByRole("button", { name: "Collapse sidebar" })
      .element()
      .querySelector("svg");

    expect(chevron).not.toBeNull();
    for (const icon of [chevron, collapse]) {
      expect(getComputedStyle(icon as Element).scale).toMatch(/^-1\b/);
    }
  });

  it("0015 AC-18: drawers open from the right", async () => {
    await setViewport(NARROW);
    const { screen } = await renderScene(table, { direction: "rtl" });

    await screen.getByRole("button", { name: "Open navigation" }).click();

    const drawer = screen.getByRole("dialog", { name: "Project" });
    await expect.element(drawer).toBeVisible();
    // The drawer may still be sliding in; wait until it rests at the right edge.
    await expect
      .poll(() => Math.round(drawer.element().getBoundingClientRect().right))
      .toBe(document.documentElement.clientWidth);
    expect(drawer.element().getBoundingClientRect().left).toBeGreaterThan(0);
  });

  it("0015 AC-6, AC-18: in forced colors the current item keeps its mark in right to left", async ({
    skip,
  }) => {
    await emulateForcedColors(true);
    try {
      // WebKit doesn't emulate forced colors (Safari has no such mode).
      if (!window.matchMedia("(forced-colors: active)").matches) skip();
      const { screen } = await renderScene(table, { direction: "rtl" });
      const current = screen
        .getByRole("navigation", { name: "Project" })
        .getByRole("link", { name: "Databases" });

      await expect.element(current).toHaveAttribute("aria-current", "page");
      // The mark is an inset outline (THM-6), so it neither moves the item nor depends on the direction.
      const style = getComputedStyle(current.element());
      expect({ style: style.outlineStyle, width: style.outlineWidth }).toEqual({
        style: "solid",
        width: "2px",
      });
    } finally {
      await emulateForcedColors(false);
    }
  });
});
