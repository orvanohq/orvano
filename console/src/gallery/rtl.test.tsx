import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { emulateForcedColors } from "@/test/media";
import { longProjectName } from "./fixtures";
import {
  NARROW,
  renderScene,
  resetShellState,
  setViewport,
  WIDE,
  focusElement,
} from "./render-scene";

// Spec 0015 AC-18, RTL-1: the project shell with dir="rtl" and React Aria's real locale ar, in three engines. Not the
// pseudo ar-XB: CI's WebKit resolves it as left to right.

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
    // Its Close button sits at the inline end: the left edge.
    const close = drawer.getByRole("button", { name: "Close" }).element().getBoundingClientRect();
    expect(close.left - drawer.element().getBoundingClientRect().left).toBeLessThanOrEqual(16);
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

  it("0015 AC-18, RTL-3: a long Latin name in the resource list keeps its start visible", async () => {
    const { screen } = await renderScene(table, { direction: "rtl" });
    const name = "audit_log_entries_with_a_very_long_name_that_truncates";
    const link = screen.getByRole("navigation", { name: "Tables" }).getByRole("link", { name });

    await expect.element(link).toBeVisible();
    expectStartVisible(link.element().querySelector("[dir=auto]"), name);
  });

  it("0015 RTL-3: switcher names and crumbs are direction isolates, and a long name keeps its start", async () => {
    const { screen } = await renderScene(table, { direction: "rtl" });
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });
    const trigger = breadcrumb.getByRole("button", { name: "Switch project: Storefront" });

    await expect.element(trigger).toBeVisible();
    const name = trigger.element().querySelector("[dir=auto]");
    expect(name?.getAttribute("title")).toBe("Storefront");
    expect(getComputedStyle(name as Element).direction).toBe("ltr");
    const current = breadcrumb.getByText("users").element();
    expect(getComputedStyle(current).direction).toBe("ltr");

    await trigger.click();
    const option = screen.getByRole("option", { name: longProjectName });
    await expect.element(option).toBeVisible();
    expectStartVisible(option.element().querySelector("[dir=auto]"), longProjectName);
  });

  it("0015 SHL-14: the route tabs scroll the current tab into view, with a fade at the cut edge", async () => {
    await setViewport({ width: 360, height: 640 });
    const { screen } = await renderScene(`${table}/settings`, { direction: "rtl" });
    const tabs = screen.getByRole("navigation", { name: "Table" });
    const current = tabs.getByRole("link", { name: "Settings" });

    await expect.element(current).toHaveAttribute("aria-current", "page");
    await expect.element(tabs).toHaveAttribute("data-cut-start");
    await expect.element(tabs).not.toHaveAttribute("data-cut-end");
    const row = tabs.element().getBoundingClientRect();
    const tab = current.element().getBoundingClientRect();
    // Right to left, the end is the left edge.
    // A pixel of slack: scroll positions round to whole pixels.
    expect(tab.left).toBeGreaterThanOrEqual(row.left - 1);
    expect(tab.right).toBeLessThanOrEqual(row.right + 1);
    expect(getComputedStyle(tabs.element()).maskImage).toContain("to left");
  });
});

/**
 * RTL-3: `element` sets its own direction (left to right for a Latin name) and truncates, cutting its end, so its
 * first letter is inside its box.
 */
function expectStartVisible(element: Element | null, text: string) {
  expect(element).not.toBeNull();
  const box = (element as HTMLElement).getBoundingClientRect();
  expect(getComputedStyle(element as Element).direction).toBe("ltr");
  expect((element as HTMLElement).scrollWidth).toBeGreaterThan(
    (element as HTMLElement).clientWidth,
  );
  const textNode = (element as HTMLElement).firstChild as Text;
  expect(textNode.data).toBe(text);
  const range = document.createRange();
  range.setStart(textNode, 0);
  range.setEnd(textNode, 1);
  const first = range.getBoundingClientRect();
  expect(first.left).toBeGreaterThanOrEqual(Math.floor(box.left));
  expect(first.right).toBeLessThanOrEqual(Math.ceil(box.right));
}
