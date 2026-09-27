import {
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
} from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { useState, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ErrorPanel } from '@/shell/error-panel'

interface DataTableProps<T> {
  /** Names the scroll container, which is focusable so keyboard users can scroll a wide table. */
  label: string
  columns: ColumnDef<T>[]
  data: readonly T[]
  /** First load: shows skeleton rows. */
  loading?: boolean
  /** A failed load: shows the error panel instead of rows. */
  error?: unknown
  onRetry?: () => void
  /** What to show when there are no rows (an `Empty` with its action slot). */
  empty?: ReactNode
  /** Cursor paging: shows "Load more" while more pages exist. */
  hasMore?: boolean
  loadingMore?: boolean
  onLoadMore?: () => void
  /** Client side sorting over the loaded rows, from the header buttons. */
  sortable?: boolean
}

const skeletonRows = 5

/**
 * The console table: TanStack Table logic under shadcn's `Table`. Handles loading, empty, error,
 * "Load more" for cursor paging, optional sorting over loaded rows, and a sticky header. It
 * scrolls inside its own labelled, focusable container so a page never scrolls sideways (AC-17).
 */
export function DataTable<T>({
  label,
  columns,
  data,
  loading = false,
  error,
  onRetry,
  empty,
  hasMore = false,
  loadingMore = false,
  onLoadMore,
  sortable = false,
}: DataTableProps<T>) {
  const [sorting, setSorting] = useState<SortingState>([])
  // TanStack Table's functions are not memoizable; nothing here is passed to a memoized child.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data: data as T[],
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: sortable ? getSortedRowModel() : undefined,
    enableSorting: sortable,
  })

  if (error !== undefined && error !== null) {
    return <ErrorPanel error={error} onRetry={onRetry ?? (() => undefined)} />
  }

  const rows = table.getRowModel().rows
  if (!loading && rows.length === 0 && empty !== undefined) return <>{empty}</>

  return (
    <div className="flex flex-col gap-3">
      <div
        role="region"
        aria-label={label}
        tabIndex={0}
        aria-busy={loading || undefined}
        className="max-w-full overflow-auto rounded-lg border border-border"
      >
        {/* The region above is the one scroller: focusable and labelled, so a keyboard user can
            scroll a wide table. `Table`'s own wrapper must not scroll instead. */}
        <Table className="tabular-nums" containerClassName="overflow-visible">
          <TableHeader className="sticky top-0 z-(--z-sticky) bg-card">
            {table.getHeaderGroups().map((group) => (
              <TableRow key={group.id}>
                {group.headers.map((header) => {
                  const sorted = header.column.getIsSorted()
                  return (
                    <TableHead
                      key={header.id}
                      aria-sort={
                        sorted === 'asc'
                          ? 'ascending'
                          : sorted === 'desc'
                            ? 'descending'
                            : undefined
                      }
                    >
                      {header.isPlaceholder ? null : header.column.getCanSort() ? (
                        <Button
                          variant="ghost"
                          size="sm"
                          className="-ml-2"
                          onClick={header.column.getToggleSortingHandler()}
                        >
                          {flexRender(header.column.columnDef.header, header.getContext())}
                          {sorted === 'asc' ? (
                            <ArrowUp aria-hidden />
                          ) : sorted === 'desc' ? (
                            <ArrowDown aria-hidden />
                          ) : (
                            <ArrowUpDown aria-hidden className="text-muted-foreground" />
                          )}
                        </Button>
                      ) : (
                        flexRender(header.column.columnDef.header, header.getContext())
                      )}
                    </TableHead>
                  )
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {loading
              ? Array.from({ length: skeletonRows }, (_, row) => (
                  <TableRow key={row} className="h-(--row-h)">
                    {columns.map((_column, col) => (
                      <TableCell key={col}>
                        <Skeleton aria-hidden className="h-4 w-3/4" />
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              : rows.map((row) => (
                  <TableRow key={row.id} className="h-(--row-h)">
                    {row.getVisibleCells().map((cell) => (
                      <TableCell key={cell.id}>
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
          </TableBody>
        </Table>
      </div>
      {hasMore ? (
        <div className="flex justify-center">
          <Button variant="outline" loading={loadingMore} onClick={onLoadMore}>
            Load more
          </Button>
        </div>
      ) : null}
    </div>
  )
}
