import { useEffect, useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { ProjectSwitcher } from "./switcher";
import type { SwitcherItem, SwitcherSearch } from "./types";

// Spec 0015 SHL-12, AC-9: the switcher's search timing, cancelation, states, and announcement.

const item = (name: string): SwitcherItem => ({
  id: name.toLowerCase(),
  name,
  link: { to: "/projects/$projectId" as "/", params: { projectId: name.toLowerCase() } },
});
const all = ["Storefront", "Billing", "Analytics"].map(item);

/** A search that resolves at once with the matching items, recording each call and whether it was aborted. */
function fakeSearch(results: (query: string) => Promise<readonly SwitcherItem[]>) {
  const calls: { query: string; limit: number; signal: AbortSignal; at: number }[] = [];
  const search = vi.fn<SwitcherSearch>((query, options) => {
    calls.push({ query, ...options, at: performance.now() });
    return results(query);
  });
  return { search, calls };
}

const matching = (query: string) =>
  Promise.resolve(all.filter((entry) => entry.name.toLowerCase().includes(query.toLowerCase())));

/** The live announcer's region (React Aria keeps one for the page). */
const announced = () =>
  Array.from(document.querySelectorAll("[aria-live='polite']"))
    .map((node) => node.textContent)
    .join(" ");

async function renderSwitcher(search: SwitcherSearch, currentId?: string) {
  return renderInRouter(
    <ProjectSwitcher
      name="Storefront"
      currentId={currentId}
      search={search}
      links={{ all: { to: "/projects" as "/" }, create: { to: "/projects/new" as "/" } }}
    />,
    [{ path: "/projects" }, { path: "/projects/new" }, { path: "/projects/$projectId" }],
  );
}

describe("ProjectSwitcher", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("0015 AC-9: loads at once with an empty query, at most 20 results, and announces the count", async () => {
    const { search, calls } = fakeSearch(matching);
    const { screen } = await renderSwitcher(search);

    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();

    const list = screen.getByRole("listbox", { name: "Projects" });
    await expect.element(list.getByRole("option", { name: "Analytics" })).toBeVisible();
    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ query: "", limit: 20 });
    await expect.poll(announced).toContain("3 projects");
    await expect.element(screen.getByRole("searchbox", { name: "Search projects" })).toHaveFocus();
    await expect.element(screen.getByRole("link", { name: "All projects" })).toBeVisible();
    await expect.element(screen.getByRole("link", { name: "New project" })).toBeVisible();
    await expectNoAxeViolations();
  });

  it("0015 AC-9: searches 150 ms after typing stops, aborting the search before", async () => {
    const { search, calls } = fakeSearch(matching);
    const { screen } = await renderSwitcher(search);
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();
    await expect.poll(() => calls.length).toBe(1);

    // fill types the whole word in one input event; key by key typing over the test bridge can be slower than 150 ms.
    const typedAt = performance.now();
    await screen.getByRole("searchbox", { name: "Search projects" }).fill("bil");

    await expect.poll(() => screen.getByRole("option").elements().length).toBe(1);
    await expect.element(screen.getByRole("option", { name: "Billing" })).toBeVisible();
    // One search for the whole word, at least 150 ms after typing.
    expect(calls.map((call) => call.query)).toEqual(["", "bil"]);
    expect(calls[1]?.at ?? 0).toBeGreaterThanOrEqual(typedAt + 150);
    await expect.poll(announced).toContain("1 project");
  });

  it("0015 AC-9: aborts a slow search when the query changes", async () => {
    const pending: ((items: readonly SwitcherItem[]) => void)[] = [];
    const { search, calls } = fakeSearch((query) =>
      query === "s" ? new Promise((resolve) => pending.push(resolve)) : matching(query),
    );
    const { screen } = await renderSwitcher(search);
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();
    await expect.poll(() => calls.length).toBe(1);

    const box = screen.getByRole("searchbox", { name: "Search projects" });
    await box.fill("s");
    await expect.poll(() => calls.length).toBe(2);
    await box.fill("st");
    await expect.poll(() => calls.length).toBe(3);

    expect(calls[1]?.signal.aborted).toBe(true);
    expect(calls[2]?.signal.aborted).toBe(false);
    pending[0]?.([item("Stale")]);
    await expect.element(screen.getByRole("option", { name: "Storefront" })).toBeVisible();
    expect(screen.getByRole("option", { name: "Stale" }).elements()).toHaveLength(0);
  });

  it("0015 AC-9: shows empty, and an error with Retry below the list", async () => {
    let fail = true;
    const { search } = fakeSearch((query) =>
      fail ? Promise.reject(new Error("offline")) : matching(query),
    );
    const { screen } = await renderSwitcher(search);
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();

    await expect
      .element(screen.getByText("Couldn't load projects. Check your connection and try again."))
      .toBeVisible();
    expect(screen.getByRole("option").elements()).toHaveLength(0);
    await expectNoAxeViolations();

    fail = false;
    await screen.getByRole("button", { name: "Retry" }).click();
    await expect.element(screen.getByRole("option", { name: "Billing" })).toBeVisible();

    await screen.getByRole("searchbox", { name: "Search projects" }).fill("zzz");
    await expect.element(screen.getByText("No projects match")).toBeVisible();
  });

  it("0015 AC-9: choosing a result navigates and closes the switcher", async () => {
    const { search } = fakeSearch(matching);
    const { router, screen } = await renderSwitcher(search);
    const button = screen.getByRole("button", { name: "Switch project: Storefront" });
    await button.click();
    await expect.element(screen.getByRole("option", { name: "Billing" })).toBeVisible();

    await userEvent.keyboard("{ArrowDown}{ArrowDown}{Enter}");

    await expect.poll(() => router.state.location.pathname).toBe("/projects/billing");
    await expect.element(screen.getByRole("dialog")).not.toBeInTheDocument();
  });

  it("0015 AC-9: marks the current project with a check and aria-current, and the highlight starts on it", async () => {
    const { search } = fakeSearch(matching);
    const { screen } = await renderSwitcher(search, "billing");
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();

    const current = screen.getByRole("option", { name: "Billing" });
    await expect.element(current).toHaveAttribute("aria-current", "true");
    expect(current.element().querySelector("svg")).not.toBeNull();
    expect(
      screen.getByRole("option", { name: "Storefront" }).element().hasAttribute("aria-current"),
    ).toBe(false);
    // The search box keeps focus; the highlight is its active descendant.
    const box = screen.getByRole("searchbox", { name: "Search projects" });
    await expect.element(box).toHaveFocus();
    await expect
      .poll(() => box.element().getAttribute("aria-activedescendant"))
      .toBe(current.element().id);
    await expectNoAxeViolations();
  });

  it("0015 AC-12: announces a search error once per failure, then the retry's result", async () => {
    let fail = true;
    const { search } = fakeSearch((query) =>
      fail ? Promise.reject(new Error("offline")) : matching(query),
    );
    const { screen } = await renderSwitcher(search);
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();

    const error = "Couldn't load projects. Check your connection and try again.";
    await expect.poll(announced).toContain(error);
    // No new live region: the message under the list is plain text (AC-12).
    expect(screen.getByRole("alert").elements()).toHaveLength(0);

    fail = false;
    await screen.getByRole("button", { name: "Retry" }).click();
    await expect.poll(announced).toContain("3 projects");
  });

  it("0015 AC-9: a new search function on re-render does not search again", async () => {
    const { search, calls } = fakeSearch(matching);
    const host = { rerender: () => {} };
    function Host() {
      const [, setRenders] = useState(0);
      useEffect(() => {
        host.rerender = () => {
          setRenders((count) => count + 1);
        };
      });
      // A new function identity on every render, as an inline arrow in a caller would be.
      return (
        <ProjectSwitcher name="Storefront" search={(query, options) => search(query, options)} />
      );
    }
    const { screen } = await renderInRouter(<Host />, [{ path: "/projects/$projectId" }]);
    await screen.getByRole("button", { name: "Switch project: Storefront" }).click();
    await expect.element(screen.getByRole("option", { name: "Billing" })).toBeVisible();
    expect(calls).toHaveLength(1);

    host.rerender();
    host.rerender();
    await new Promise((resolve) => requestAnimationFrame(resolve));
    await expect.element(screen.getByRole("option", { name: "Billing" })).toBeVisible();

    expect(calls).toHaveLength(1);
  });
});
