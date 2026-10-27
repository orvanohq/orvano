import { msg } from "@lingui/core/macro";
import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { Button } from "./button";
import { Menu, MenuItem, MenuSection, MenuTrigger } from "./menu";
import { PageHeader } from "./page-header";
import { Tab, TabList, TabPanel, Tabs } from "./tabs";

// Spec 0015 AC-15: Menu, Tabs, and PageHeader keyboard models and axe, in three engines.

const tabUntil = async (element: Element) => {
  for (let index = 0; index < 5 && document.activeElement !== element; index++) {
    await userEvent.tab();
  }
};

describe("Menu", () => {
  it("0015 AC-15: opens from its button, moves by arrows, runs an item on Enter, and returns focus", async () => {
    const rename = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <MenuTrigger trigger={<Button>Actions</Button>}>
        <Menu aria-label="Actions">
          <MenuItem id="rename" onAction={rename}>
            Rename
          </MenuItem>
          <MenuItem id="duplicate">Duplicate</MenuItem>
        </Menu>
      </MenuTrigger>,
    );
    const button = screen.getByRole("button", { name: "Actions" });

    await tabUntil(button.element());
    await userEvent.keyboard("{Enter}");
    await expect.element(screen.getByRole("menuitem", { name: "Rename" })).toHaveFocus();
    await expectNoAxeViolations();
    await userEvent.keyboard("{ArrowDown}");
    await expect.element(screen.getByRole("menuitem", { name: "Duplicate" })).toHaveFocus();
    await userEvent.keyboard("{ArrowUp}{Enter}");

    expect(rename).toHaveBeenCalledOnce();
    await expect.element(screen.getByRole("menu")).not.toBeInTheDocument();
    await expect.element(button).toHaveFocus();
  });

  it("0015 AC-15: an unavailable item stays focusable, ignores Enter, and shows its reason (PRM-4)", async () => {
    const exportRows = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <MenuTrigger trigger={<Button>Actions</Button>}>
        <Menu aria-label="Actions">
          <MenuItem
            id="export"
            onAction={exportRows}
            unavailableReason={msg`You need the Admin role to export rows.`}
          >
            Export rows
          </MenuItem>
        </Menu>
      </MenuTrigger>,
    );

    await tabUntil(screen.getByRole("button", { name: "Actions" }).element());
    await userEvent.keyboard("{Enter}");
    const item = screen.getByRole("menuitem", { name: "Export rows" });
    await expect.element(item).toHaveFocus();
    await expect.element(item).toHaveAttribute("aria-disabled", "true");
    await expect
      .element(item)
      .toHaveAccessibleDescription("You need the Admin role to export rows.");
    await userEvent.keyboard("{Enter}");

    expect(exportRows).not.toHaveBeenCalled();
    await expectNoAxeViolations();
  });

  it("0015 AC-15: a selection section checks one item", async () => {
    const onSelect = vi.fn<(key: string) => void>();
    const { screen } = await renderInRouter(
      <MenuTrigger trigger={<Button>View</Button>}>
        <Menu aria-label="View">
          <MenuSection title="Sort" selectedKey="name" onSelect={onSelect}>
            <MenuItem id="name">By name</MenuItem>
            <MenuItem id="date">By date</MenuItem>
          </MenuSection>
        </Menu>
      </MenuTrigger>,
    );

    await screen.getByRole("button", { name: "View" }).click();
    await expect.element(screen.getByRole("group", { name: "Sort" })).toBeVisible();
    await expect
      .element(screen.getByRole("menuitemradio", { name: "By name" }))
      .toHaveAttribute("aria-checked", "true");
    await screen.getByRole("menuitemradio", { name: "By date" }).click();

    expect(onSelect).toHaveBeenCalledExactlyOnceWith("date");
  });
});

describe("Tabs", () => {
  it("0015 AC-15: arrows move focus, and Enter selects (manual activation, UX-5)", async () => {
    const { screen } = await renderInRouter(
      <Tabs>
        <TabList aria-label="SDK">
          <Tab id="js">JavaScript</Tab>
          <Tab id="dart">Dart</Tab>
        </TabList>
        <TabPanel id="js">npm install</TabPanel>
        <TabPanel id="dart">dart pub add</TabPanel>
      </Tabs>,
    );
    const js = screen.getByRole("tab", { name: "JavaScript" });
    const dart = screen.getByRole("tab", { name: "Dart" });

    await tabUntil(js.element());
    await userEvent.keyboard("{ArrowRight}");
    await expect.element(dart).toHaveFocus();
    await expect.element(js).toHaveAttribute("aria-selected", "true");
    await userEvent.keyboard("{Enter}");

    await expect.element(dart).toHaveAttribute("aria-selected", "true");
    await expect.element(screen.getByRole("tabpanel")).toHaveTextContent("dart pub add");
    await userEvent.keyboard("{Home}");
    await expect.element(js).toHaveFocus();
    await expectNoAxeViolations();
  });
});

describe("PageHeader", () => {
  it("0015 AC-15: renders the page's h1, focusable by script, with its actions", async () => {
    const { screen } = await renderInRouter(
      <PageHeader
        title="users"
        description="A table."
        actions={<Button variant="primary">Insert row</Button>}
      />,
    );

    const heading = screen.getByRole("heading", { level: 1, name: "users" });
    await expect.element(heading).toHaveAttribute("tabindex", "-1");
    await expect.element(screen.getByRole("button", { name: "Insert row" })).toBeVisible();
    await expectNoAxeViolations();
  });
});
