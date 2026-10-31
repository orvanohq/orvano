import type { ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import {
  Cell as AriaCell,
  CheckboxButton,
  CheckboxField,
  Column as AriaColumn,
  Row as AriaRow,
  Table as AriaTable,
  TableBody as AriaTableBody,
  TableHeader as AriaTableHeader,
  useTableOptions,
  type Key,
  type Selection,
  type SortDescriptor,
} from "react-aria-components";
import { cx, focusRingInset } from "../cx";
import { checkboxBox } from "../field-styles";
import { Icon } from "../icon";
import { CheckIcon, IndeterminateIcon, SortAscendingIcon, SortDescendingIcon } from "../icons";
import { VisuallyHidden } from "../visually-hidden";

// design.md _Data display_: a static React Aria table for short lists (API keys, members), lazy (PRM-10): import it
// from `@/ui/table`, never the `@/ui` index. Grids of rows use `DataGrid` (the rows feature, PRM-5).

export type { Key, Selection, SortDescriptor };
export { Pagination, type PaginationProps } from "./pagination";

export interface TableProps {
  /** Required: names the table for screen readers ("API keys"). */
  "aria-label": string;
  children: ReactNode;
  sortDescriptor?: SortDescriptor;
  onSortChange?: (descriptor: SortDescriptor) => void;
  /** Rows get a checkbox; `multiple` adds "Select all" in the header. */
  selectionMode?: "none" | "single" | "multiple";
  selectedKeys?: Selection;
  onSelectionChange?: (keys: Selection) => void;
  /** Opens a row (its detail page) on Enter or a click. */
  onRowAction?: (key: Key) => void;
}

/**
 * Arrow keys move between rows and cells (APG grid); rows follow the density's height (UX-32). The table scrolls in
 * its own container, a region named like the table so its scrolling is announced (SRT-13). Scroll padding the height
 * of the sticky header keeps a focused row out from under it (SHL-8, WCAG 2.4.11), and bottom padding keeps an
 * overlay scrollbar off the last row.
 */
export function Table({
  "aria-label": label,
  children,
  selectionMode = "none",
  ...props
}: TableProps) {
  return (
    <section
      aria-label={label}
      className="min-h-0 min-w-0 scroll-pt-(--row-height) overflow-auto rounded-md border pb-3"
    >
      <AriaTable
        {...props}
        aria-label={label}
        selectionMode={selectionMode}
        selectionBehavior="toggle"
        className="w-full border-separate border-spacing-0 text-start"
      >
        {children}
      </AriaTable>
    </section>
  );
}

function SelectionCheckbox() {
  return (
    <CheckboxField slot="selection" className="flex">
      <CheckboxButton className="group flex size-6 cursor-default items-center justify-center outline-none">
        {({ isSelected, isIndeterminate }) => (
          <span className={checkboxBox}>
            {isIndeterminate ? (
              <Icon icon={IndeterminateIcon} />
            ) : isSelected ? (
              <Icon icon={CheckIcon} />
            ) : null}
          </span>
        )}
      </CheckboxButton>
    </CheckboxField>
  );
}

const headerCell = cx(
  "sticky top-0 z-(--z-sticky) h-(--row-height) border-b bg-bg-muted px-3 text-start text-sm font-medium whitespace-nowrap text-fg-muted",
  focusRingInset,
);

/** The header row. Put `Column`s in it. */
export function TableHeader({ children }: { children: ReactNode }) {
  const { t } = useLingui();
  const { selectionBehavior, selectionMode } = useTableOptions();
  return (
    <AriaTableHeader>
      {selectionBehavior === "toggle" && selectionMode !== "none" ? (
        <AriaColumn className={cx(headerCell, "w-10")}>
          {selectionMode === "multiple" ? (
            <SelectionCheckbox />
          ) : (
            <VisuallyHidden>{t`Select`}</VisuallyHidden>
          )}
        </AriaColumn>
      ) : null}
      {children}
    </AriaTableHeader>
  );
}

export interface ColumnProps {
  id: Key;
  children: string;
  /** The column that names each row (screen readers read it as the row's name). One per table. */
  isRowHeader?: boolean;
  allowsSorting?: boolean;
}

/** A header cell. A sortable one shows the direction and sorts on Enter or a click. */
export function Column({ id, children, isRowHeader, allowsSorting }: ColumnProps) {
  return (
    <AriaColumn
      id={id}
      isRowHeader={isRowHeader}
      allowsSorting={allowsSorting}
      className={cx(headerCell, allowsSorting && "cursor-default data-hovered:text-fg")}
    >
      {({ sortDirection }) => (
        <span className="inline-flex items-center gap-1">
          {children}
          {sortDirection ? (
            <Icon icon={sortDirection === "ascending" ? SortAscendingIcon : SortDescendingIcon} />
          ) : null}
        </span>
      )}
    </AriaColumn>
  );
}

export interface TableBodyProps {
  children: ReactNode;
  /** Shown in place of rows when there are none: an `EmptyState` (UX-20). */
  emptyState?: ReactNode;
}

export function TableBody({ children, emptyState }: TableBodyProps) {
  return (
    <AriaTableBody renderEmptyState={emptyState ? () => emptyState : undefined}>
      {children}
    </AriaTableBody>
  );
}

/** One row. Put one `Cell` in it per `Column`. */
export function Row({ id, children }: { id: Key; children: ReactNode }) {
  const { selectionBehavior, selectionMode } = useTableOptions();
  return (
    <AriaRow
      id={id}
      className={cx(
        "group/row h-(--row-height) cursor-default text-fg data-hovered:bg-bg-muted data-selected:bg-accent-subtle data-selected:text-accent-subtle-fg",
        focusRingInset,
      )}
    >
      {selectionBehavior === "toggle" && selectionMode !== "none" ? (
        <AriaCell className="border-b px-2 group-last/row:border-b-0">
          <SelectionCheckbox />
        </AriaCell>
      ) : null}
      {children}
    </AriaRow>
  );
}

/**
 * One cell. Text is the user's (a name, a key, an endpoint), so it sits in a `bdi` direction isolate: a Latin name in
 * a right to left table keeps its own order (RTL-3). Other content (a chip, a time) sets its own.
 */
export function Cell({ children }: { children: ReactNode }) {
  return (
    <AriaCell
      // Cells never wrap, so a row keeps the density's height (UX-32) and a masked key stays on one line: a narrow
      // screen scrolls the table sideways instead.
      className={cx(
        "border-b px-3 align-middle whitespace-nowrap group-last/row:border-b-0",
        focusRingInset,
      )}
    >
      {typeof children === "string" || typeof children === "number" ? (
        <bdi>{children}</bdi>
      ) : (
        children
      )}
    </AriaCell>
  );
}
