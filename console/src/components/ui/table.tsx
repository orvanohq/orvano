import * as React from "react";
import { cn } from "cn";

/**
 * Tables: 36px rows, caption type headers on surface-subtle, border dividers. Numbers use `tabular-nums` and
 * right alignment, IDs use mono. Wrap a table that scrolls in `TableScroll`, which keyboard users can scroll.
 */
function Table({ className, ...props }: React.ComponentProps<"table">) {
  return (
    <table
      data-slot="table"
      className={cn("w-full caption-bottom text-sm", className)}
      {...props}
    />
  );
}

/** A focusable, labelled scroll area with a max height, so the sticky header shows and the keyboard can scroll it. */
function TableScroll({
  className,
  label,
  ...props
}: React.ComponentProps<"div"> & { label: string }) {
  return (
    <div
      data-slot="table-scroll"
      // A scrollable region must be reachable by keyboard (WCAG 2.1.1); its name says which table it holds.
      tabIndex={0}
      role="region"
      aria-label={label}
      className={cn(
        "relative w-full overflow-auto rounded-md border border-border bg-surface outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
        className,
      )}
      {...props}
    />
  );
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
  return (
    <thead
      data-slot="table-header"
      className={cn(
        "sticky top-0 z-10 bg-surface-subtle [&_tr]:border-b",
        className,
      )}
      {...props}
    />
  );
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0", className)}
      {...props}
    />
  );
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        "h-9 border-b border-border transition-colors hover:bg-surface-subtle",
        className,
      )}
      {...props}
    />
  );
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-9 px-3 text-left align-middle text-xs font-medium whitespace-nowrap text-ink-muted",
        className,
      )}
      {...props}
    />
  );
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
  return (
    <td
      data-slot="table-cell"
      className={cn("px-3 py-2 align-middle whitespace-nowrap", className)}
      {...props}
    />
  );
}

function TableCaption({
  className,
  ...props
}: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("mt-2 text-small text-ink-muted", className)}
      {...props}
    />
  );
}

export {
  Table,
  TableScroll,
  TableHeader,
  TableBody,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
};
