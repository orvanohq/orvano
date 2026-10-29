import { describe, expect, it } from "vitest";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { Breadcrumbs } from "./breadcrumbs";
import type { Crumb, ShellModel } from "./types";

// Spec 0015 AC-9, SHL-4: the crumbs' current marks and the collapsed menu. The shell's widths are in
// src/gallery/shell.test.tsx.

const none = () => Promise.resolve([]);
const model: ShellModel = {
  user: { id: "usr_ada", name: "Ada Lovelace", email: "ada@example.com" },
  scope: "org",
  org: { id: "acme", name: "Acme" },
  nav: [],
  searchOrgs: none,
  searchProjects: none,
  onSignOut: () => undefined,
};

describe("Breadcrumbs", () => {
  it("0015 AC-9: an org crumb in last position is the current page", async () => {
    const { screen } = await renderInRouter(
      <Breadcrumbs model={model} crumbs={[{ kind: "org" }]} />,
    );

    await expect
      .element(screen.getByRole("button", { name: "Switch organization: Acme" }))
      .toHaveAttribute("aria-current", "page");
  });

  it("0015 AC-9: a collapsed page crumb with no link is text in the menu, not a dead item", async () => {
    const crumbs: Crumb[] = [
      { kind: "org" },
      { kind: "page", label: "Settings" },
      { kind: "page", label: "Billing", link: { to: "/billing" } },
      { kind: "page", label: "Invoices" },
    ];
    const { screen } = await renderInRouter(
      <Breadcrumbs model={model} crumbs={crumbs} collapsed />,
      [{ path: "/billing" }],
    );

    await screen.getByRole("button", { name: "Show the full path" }).click();
    const menu = screen.getByRole("menu");
    await expect.element(menu.getByRole("menuitem", { name: "Billing" })).toBeVisible();
    await expect.element(menu.getByText("Settings")).toBeVisible();
    expect(menu.getByRole("menuitem", { name: "Settings" }).elements()).toHaveLength(0);
    await expectNoAxeViolations();
  });
});
