import { afterEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { messages } from "@/locales/en/messages.po";
import { activateLocale, i18n } from "@/shared/i18n";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { FILTER_SETTLE_MS, ResourceList, type ResourceListItem } from "./resource-list";

// Spec 0001 UX-2, UX-21, UX-41, design.md _Shell_: the resource list's filter announces its match count once typing
// settles, lower cases for the active locale, and shows a skeleton while the resources load.

const item = (name: string): ResourceListItem => ({ id: name, name, link: { to: "/" } });
const tables = ["orders", "users", "payments"].map(item);

/** The text in React Aria's live announcer (the one region every announcement goes through, UX-41). */
const announced = () =>
  Array.from(document.querySelectorAll("[aria-live]"))
    .map((node) => node.textContent)
    .join(" ");

function List({ items }: { items: readonly ResourceListItem[] | undefined }) {
  return <ResourceList items={items} filterLabel="Filter tables" emptyMessage="No tables yet" />;
}

const links = () => Array.from(document.querySelectorAll("li a")).map((link) => link.textContent);

afterEach(async () => {
  await activateLocale("en");
});

describe("ResourceList", () => {
  it("0001 UX-41: announces how many match once typing settles, not on each key or on load", async () => {
    const { screen } = await renderInRouter(<List items={tables} />);
    const filter = screen.getByRole("searchbox", { name: "Filter tables" });
    await expect.element(filter).toBeVisible();
    expect(announced()).not.toMatch(/match/);

    await filter.click();
    await userEvent.keyboard("r");

    expect(links()).toEqual(["orders", "users"]);
    // Nothing yet: the count waits for the pause after the last key.
    expect(announced()).not.toMatch(/match/);
    await expect.poll(announced, { timeout: FILTER_SETTLE_MS + 2000 }).toMatch(/2 matches/);

    await userEvent.keyboard("d");
    expect(links()).toEqual(["orders"]);
    await expect.poll(announced, { timeout: FILTER_SETTLE_MS + 2000 }).toMatch(/1 match\b/);
    await expectNoAxeViolations();
  });

  it("0003 I18N-5: filters with the active locale's case rules (Turkish İ)", async () => {
    // No Turkish catalog ships, so Turkish is activated with the English strings: only the locale's rules matter.
    i18n.loadAndActivate({ locale: "tr", messages });
    const { screen } = await renderInRouter(<List items={["İstanbul", "Irmak"].map(item)} />);
    const filter = screen.getByRole("searchbox");
    await filter.click();

    // In Turkish "i" is the lower case of "İ", not of "I" (whose lower case is "ı").
    await userEvent.keyboard("i");
    expect(i18n.locale).toBe("tr");
    await expect.poll(links).toEqual(["İstanbul"]);
  });

  it("0001 UX-21: while loading the list is busy, with a skeleton only after the delay", async () => {
    const { screen } = await renderInRouter(<List items={undefined} />);
    await expect.element(screen.getByRole("searchbox")).toBeVisible();

    const busy = document.querySelector("[aria-busy='true']");
    expect(busy).not.toBeNull();
    expect(busy?.textContent).toBe("");
    await expect.poll(() => busy?.textContent, { timeout: 2000 }).toBe("Loading");
    expect(document.body.textContent).not.toContain("Loading…");
  });
});
