import { useState } from "react";
import { describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { EmptyState } from "../states";
import { Cell, Column, Row, Table, TableBody, TableHeader, type SortDescriptor } from ".";

// Spec 0015 AC-15: the static table (APG grid keys: arrows between rows and cells), sorting, selection, the empty
// state, and rows at the density's height (UX-32), in three engines.

const keys = [
  { id: "ci", name: "CI deploys" },
  { id: "mobile", name: "Mobile app" },
  { id: "old", name: "Old server" },
];

function KeysTable({ selectionMode }: { selectionMode?: "none" | "multiple" }) {
  const [sort, setSort] = useState<SortDescriptor>({ column: "name", direction: "ascending" });
  const sorted = keys.toSorted((a, b) =>
    sort.direction === "ascending" ? a.name.localeCompare(b.name) : b.name.localeCompare(a.name),
  );
  return (
    <Table
      aria-label="API keys"
      sortDescriptor={sort}
      onSortChange={setSort}
      selectionMode={selectionMode}
    >
      <TableHeader>
        <Column id="name" isRowHeader allowsSorting>
          Name
        </Column>
        <Column id="id">ID</Column>
      </TableHeader>
      <TableBody>
        {sorted.map((key) => (
          <Row key={key.id} id={key.id}>
            <Cell>{key.name}</Cell>
            <Cell>{key.id}</Cell>
          </Row>
        ))}
      </TableBody>
    </Table>
  );
}

function OneRow({ name, isBusy }: { name: string; isBusy: boolean }) {
  return (
    <Table aria-label={name} isBusy={isBusy}>
      <TableHeader>
        <Column id="name" isRowHeader>
          Name
        </Column>
      </TableHeader>
      <TableBody>
        <Row id="ci">
          <Cell>CI deploys</Cell>
        </Row>
      </TableBody>
    </Table>
  );
}

const rowNames = () =>
  Array.from(document.querySelectorAll("tbody [role='rowheader']")).map((cell) => cell.textContent);

describe("Table", () => {
  it("0015 AC-15: arrow keys move between rows, and passes axe in both themes and densities", async () => {
    const { screen } = await renderInRouter(<KeysTable />);
    const grid = screen.getByRole("grid", { name: "API keys" });
    await expect.element(grid).toBeVisible();

    const rows = screen.getByRole("row");
    await focusElement(rows.nth(1));
    await userEvent.keyboard("{ArrowDown}");
    await expect.element(rows.nth(2)).toHaveFocus();
    await userEvent.keyboard("{ArrowUp}");
    await expect.element(rows.nth(1)).toHaveFocus();

    for (const theme of ["light", "dark"]) {
      for (const density of ["comfortable", "compact"]) {
        document.documentElement.dataset.theme = theme;
        document.documentElement.dataset.density = density;
        // oxlint-disable-next-line no-await-in-loop -- axe runs one combination at a time
        await expectNoAxeViolations();
      }
    }
    delete document.documentElement.dataset.theme;
    delete document.documentElement.dataset.density;
  });

  it("0015 AC-15: rows follow the density's row height (UX-32)", async () => {
    const { screen } = await renderInRouter(<KeysTable />);
    const row = screen.getByRole("row").nth(1);
    await expect.element(row).toBeVisible();

    expect((row.element() as HTMLElement).getBoundingClientRect().height).toBe(40);
    document.documentElement.dataset.density = "compact";
    expect((row.element() as HTMLElement).getBoundingClientRect().height).toBe(28);
    delete document.documentElement.dataset.density;
  });

  it("0015 AC-15: a sortable column sorts on Enter and shows its direction", async () => {
    const { screen } = await renderInRouter(<KeysTable />);
    const name = screen.getByRole("columnheader", { name: "Name" });
    await expect.element(name).toHaveAttribute("aria-sort", "ascending");
    expect(rowNames()).toEqual(["CI deploys", "Mobile app", "Old server"]);

    await focusElement(name);
    await userEvent.keyboard("{Enter}");

    await expect.element(name).toHaveAttribute("aria-sort", "descending");
    expect(rowNames()).toEqual(["Old server", "Mobile app", "CI deploys"]);
  });

  it("0015 AC-15: selectable rows have checkboxes and Select All, and Space selects", async () => {
    const { screen } = await renderInRouter(<KeysTable selectionMode="multiple" />);
    const all = screen.getByRole("checkbox", { name: "Select All" });
    await expect.element(all).toBeInTheDocument();
    expect(screen.getByRole("checkbox").elements()).toHaveLength(4);

    await focusElement(screen.getByRole("row").nth(1));
    await userEvent.keyboard(" ");

    await expect.element(screen.getByRole("row").nth(1)).toHaveAttribute("aria-selected", "true");
    await expectNoAxeViolations();
  });

  it("0015 SHL-8: the sticky header never covers the focused row in a short scroll container", async () => {
    const many = Array.from({ length: 20 }, (_, index) => ({
      id: `key${String(index)}`,
      name: `Key ${String(index)}`,
    }));
    const { screen } = await renderInRouter(
      // A caller that gives the table less height than its rows: the table's own container scrolls.
      <div style={{ display: "flex", flexDirection: "column", height: 200 }}>
        <Table aria-label="API keys">
          <TableHeader>
            <Column id="name" isRowHeader>
              Name
            </Column>
          </TableHeader>
          <TableBody>
            {many.map((key) => (
              <Row key={key.id} id={key.id}>
                <Cell>{key.name}</Cell>
              </Row>
            ))}
          </TableBody>
        </Table>
      </div>,
    );
    const region = screen.getByRole("region", { name: "API keys" });
    await expect.element(region).toBeVisible();
    const header = screen.getByRole("columnheader", { name: "Name" }).element();
    const scroller = region.element();

    /** The focused row is fully inside the scroller, below the sticky header. */
    const focusedRowInView = () => {
      const row = (document.activeElement as HTMLElement).getBoundingClientRect();
      return (
        row.top >= header.getBoundingClientRect().bottom - 1 &&
        row.bottom <= scroller.getBoundingClientRect().bottom + 1
      );
    };

    await focusElement(screen.getByRole("row").nth(1));
    for (const key of [
      ...Array<string>(12).fill("{ArrowDown}"),
      ...Array<string>(11).fill("{ArrowUp}"),
    ]) {
      // oxlint-disable-next-line no-await-in-loop -- each key press depends on the one before
      await userEvent.keyboard(key);
      // oxlint-disable-next-line no-await-in-loop -- checked after each press, while the container scrolls
      await expect.poll(focusedRowInView).toBe(true);
    }
    expect(scroller.scrollTop).toBeGreaterThan(0);
  });

  it("0015 AC-15: at a narrow width a masked key stays on one line and the table scrolls sideways", async () => {
    const { screen } = await renderInRouter(
      <div style={{ width: 160 }}>
        <Table aria-label="API keys">
          <TableHeader>
            <Column id="name" isRowHeader>
              Name
            </Column>
            <Column id="key">Key</Column>
          </TableHeader>
          <TableBody>
            <Row id="old">
              <Cell>Old server</Cell>
              <Cell>ok_live_7f3a…91c2</Cell>
            </Row>
          </TableBody>
        </Table>
      </div>,
    );
    const cell = screen.getByText("ok_live_7f3a…91c2");
    await expect.element(cell).toBeVisible();

    expect(
      (screen.getByRole("row").nth(1).element() as HTMLElement).getBoundingClientRect().height,
    ).toBe(40);
    const region = screen.getByRole("region", { name: "API keys" }).element();
    expect(region.scrollWidth).toBeGreaterThan(region.clientWidth);
    await expectNoAxeViolations();
  });

  it("0015 RTL-3: a text cell is a direction isolate, so a Latin name keeps its order in right to left", async () => {
    const { screen } = await renderInRouter(
      <div dir="rtl">
        <Table aria-label="API keys">
          <TableHeader>
            <Column id="name" isRowHeader>
              Name
            </Column>
            <Column id="state">State</Column>
          </TableHeader>
          <TableBody>
            <Row id="old">
              <Cell>Old server (v2)</Cell>
              <Cell>
                <span>Active</span>
              </Cell>
            </Row>
          </TableBody>
        </Table>
      </div>,
    );
    const name = screen.getByText("Old server (v2)");
    await expect.element(name).toBeVisible();

    expect(name.element().tagName).toBe("BDI");
    expect(getComputedStyle(name.element()).direction).toBe("ltr");
    // Other content sets its own direction, so it isn't wrapped.
    expect(screen.getByText("Active").element().parentElement?.tagName).not.toBe("BDI");
  });

  it("0001 UX-21: a busy table marks its region, and a table at rest isn't marked", async () => {
    const { screen } = await renderInRouter(
      <>
        <OneRow name="Loading" isBusy />
        <OneRow name="Loaded" isBusy={false} />
      </>,
    );

    await expect
      .element(screen.getByRole("region", { name: "Loading" }))
      .toHaveAttribute("aria-busy");
    await expect
      .element(screen.getByRole("region", { name: "Loaded" }))
      .not.toHaveAttribute("aria-busy");
  });

  it("0001 UX-20: an empty table shows its empty state", async () => {
    const { screen } = await renderInRouter(
      <Table aria-label="Webhooks">
        <TableHeader>
          <Column id="url" isRowHeader>
            URL
          </Column>
        </TableHeader>
        <TableBody
          emptyState={
            <EmptyState kind="first-use" title="No webhooks yet" description="Create one." />
          }
        >
          {[]}
        </TableBody>
      </Table>,
    );

    await expect.element(screen.getByRole("heading", { name: "No webhooks yet" })).toBeVisible();
    await expectNoAxeViolations();
  });
});
