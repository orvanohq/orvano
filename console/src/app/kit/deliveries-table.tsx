"use client";

import { useRef, useState } from "react";

import { CodeBlock } from "@/components/code-block";
import { Badge } from "@/components/ui/badge";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  TableScroll,
} from "@/components/ui/table";

import { deliveries, type Delivery } from "./deliveries";

const numberFormat = new Intl.NumberFormat("en-US", {
  maximumFractionDigits: 1,
});

/**
 * The table pattern: each row's ID is a real button that opens the row's side sheet, and a click anywhere
 * else on the row does the same as a mouse shortcut. The row itself gets no role.
 */
export function DeliveriesTable() {
  // The selected row stays set while the sheet animates closed, so its content doesn't vanish mid exit.
  const [selected, setSelected] = useState<Delivery | null>(null);
  const [isOpen, setIsOpen] = useState(false);
  // Radix returns focus only to a SheetTrigger. This sheet opens from a row, so focus goes back to that
  // row's ID button on close, whether the button or a click on the row opened it.
  const returnFocusTo = useRef<HTMLButtonElement | null>(null);
  const show = (delivery: Delivery, idButton: HTMLButtonElement | null) => {
    returnFocusTo.current = idButton;
    setSelected(delivery);
    setIsOpen(true);
  };

  return (
    <>
      <TableScroll label="Recent deliveries" className="max-h-80">
        <Table>
          <caption className="sr-only">Recent deliveries</caption>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead scope="col">ID</TableHead>
              <TableHead scope="col">Endpoint</TableHead>
              <TableHead scope="col">Status</TableHead>
              <TableHead scope="col" className="text-right">
                Latency
              </TableHead>
              <TableHead scope="col" className="text-right">
                Size
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {deliveries.map((delivery) => (
              <TableRow
                key={delivery.id}
                data-state={
                  isOpen && selected?.id === delivery.id
                    ? "selected"
                    : undefined
                }
                onClick={(event) =>
                  show(delivery, event.currentTarget.querySelector("button"))
                }
                className="cursor-pointer data-[state=selected]:bg-brand-subtle"
              >
                <TableCell>
                  <button
                    type="button"
                    onClick={(event) => {
                      event.stopPropagation();
                      show(delivery, event.currentTarget);
                    }}
                    className="rounded-sm font-mono text-mono-small text-ink outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                  >
                    {delivery.id}
                  </button>
                </TableCell>
                <TableCell className="max-w-72 truncate text-ink-muted">
                  {delivery.endpoint}
                </TableCell>
                <TableCell>
                  <Badge variant={delivery.status.variant} dot>
                    {delivery.status.label}
                  </Badge>
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {delivery.latencyMs} ms
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {numberFormat.format(delivery.sizeKb)} KB
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableScroll>

      <Sheet open={isOpen} onOpenChange={setIsOpen}>
        <SheetContent
          side="right"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            returnFocusTo.current?.focus();
          }}
        >
          {selected && (
            <>
              <SheetHeader className="border-b border-border pr-12">
                <SheetTitle className="font-mono">{selected.id}</SheetTitle>
                <SheetDescription>
                  A webhook delivery to {new URL(selected.endpoint).host}.
                </SheetDescription>
              </SheetHeader>
              <div className="flex flex-col gap-4 overflow-y-auto px-4 pb-4">
                <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
                  <dt className="text-ink-muted">Endpoint</dt>
                  <dd className="min-w-0 break-all text-ink">
                    {selected.endpoint}
                  </dd>
                  <dt className="text-ink-muted">Status</dt>
                  <dd>
                    <Badge variant={selected.status.variant} dot>
                      {selected.status.label}
                    </Badge>
                  </dd>
                  <dt className="text-ink-muted">Latency</dt>
                  <dd className="text-ink tabular-nums">
                    {selected.latencyMs} ms
                  </dd>
                  <dt className="text-ink-muted">Size</dt>
                  <dd className="text-ink tabular-nums">
                    {numberFormat.format(selected.sizeKb)} KB
                  </dd>
                  <dt className="text-ink-muted">Attempts</dt>
                  <dd className="text-ink tabular-nums">{selected.attempts}</dd>
                </dl>
                <CodeBlock
                  language="json"
                  code={JSON.stringify(selected, null, 2)}
                />
              </div>
            </>
          )}
        </SheetContent>
      </Sheet>
    </>
  );
}
