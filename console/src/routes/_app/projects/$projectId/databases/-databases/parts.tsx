import { Link } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { Database as DatabaseIcon, Table2 } from 'lucide-react'
import type { ReactNode } from 'react'

import { Badge } from '@/components/ui/badge'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Spinner } from '@/components/ui/spinner'
import type { Column, Database, Table } from '@orvano/console-client'

/** A row: values by column name, in their JSON forms (spec 0015, AC-16). */
export type Row = Record<string, unknown>

/** A database's status (spec 0015, AC-31): provisioning spins until it is active. */
export function DatabaseStatusBadge({ status }: { status: Database['status'] }) {
  switch (status) {
    case 'active':
      return (
        <Badge variant="status" tone="success">
          Active
        </Badge>
      )
    case 'provisioning':
      return (
        <Badge variant="status" tone="warning">
          <Spinner aria-hidden className="size-3" />
          Provisioning
        </Badge>
      )
    case 'failed':
      return (
        <Badge variant="status" tone="danger">
          Failed
        </Badge>
      )
  }
}

/** The Databases table: slug (the link to its tables), name, and status. */
export function databaseColumns(projectId: string): ColumnDef<Database>[] {
  return [
    {
      accessorKey: 'slug',
      header: 'Database',
      cell: ({ row }) => (
        <Link
          to="/projects/$projectId/databases/$database"
          params={{ projectId, database: row.original.slug }}
          className="font-mono font-medium text-link hover:underline"
        >
          {row.original.slug}
        </Link>
      ),
    },
    { accessorKey: 'name', header: 'Name' },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => <DatabaseStatusBadge status={row.original.status} />,
    },
  ]
}

/** The tables of a database: name (the link to its rows), estimated rows, and a Read only badge (AC-32). */
export function tableColumns(projectId: string, database: string): ColumnDef<Table>[] {
  return [
    {
      accessorKey: 'name',
      header: 'Table',
      cell: ({ row }) => (
        <span className="flex flex-wrap items-center gap-2">
          <Link
            to="/projects/$projectId/databases/$database/tables/$table"
            params={{ projectId, database, table: row.original.name }}
            className="font-mono font-medium text-link hover:underline"
          >
            {row.original.name}
          </Link>
          {row.original.writable ? null : <Badge>Read only</Badge>}
        </span>
      ),
    },
    {
      accessorKey: 'columns',
      header: 'Columns',
      enableSorting: false,
      cell: ({ row }) => row.original.columns.length,
    },
    {
      accessorKey: 'estimatedRows',
      header: 'Estimated rows',
      cell: ({ row }) =>
        row.original.estimatedRows === null ? (
          <span className="text-muted-foreground">Not counted yet</span>
        ) : (
          row.original.estimatedRows.toLocaleString()
        ),
    },
  ]
}

/** One cell value as text: `NULL` for null, JSON for objects and lists, the rest as they come. */
export function cellText(value: unknown): string {
  if (value === null || value === undefined) return 'NULL'
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  return JSON.stringify(value)
}

/** One grid column per table column, in table order, each cell one line that never wraps (AC-33). */
export function rowColumns(columns: readonly Column[]): ColumnDef<Row>[] {
  return columns.map((column) => ({
    id: column.name,
    accessorFn: (row: Row) => row[column.name],
    header: () => (
      <span className="flex flex-col">
        <span className="font-mono">{column.name}</span>
        <span className="text-xs font-normal text-muted-foreground">{column.pgType}</span>
      </span>
    ),
    enableSorting: false,
    cell: ({ getValue }) => {
      const value = getValue()
      const text = cellText(value)
      return (
        <span
          className={
            value === null || value === undefined
              ? 'text-muted-foreground'
              : 'block max-w-80 truncate font-mono'
          }
          title={text}
        >
          {text}
        </span>
      )
    },
  }))
}

/** A row's stable ID for the grid: its `id` when the table has the standard one. */
export function rowId(row: Row, index: number): string {
  const id = row.id
  return typeof id === 'string' || typeof id === 'number' ? String(id) : `row-${String(index)}`
}

/** The empty tables list, with New table (AC-32). */
export function NoTables({ action }: { action: ReactNode }) {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <Table2 aria-hidden />
        </EmptyMedia>
        <EmptyTitle>No tables yet</EmptyTitle>
        <EmptyDescription>
          A table holds your app&apos;s rows. Each one starts with an id, created_at, and
          updated_at.
        </EmptyDescription>
      </EmptyHeader>
      <div data-slot="empty-action">{action}</div>
    </Empty>
  )
}

/** The empty rows grid. */
export function NoRows() {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <DatabaseIcon aria-hidden />
        </EmptyMedia>
        <EmptyTitle>No rows yet</EmptyTitle>
        <EmptyDescription>
          Rows you create from a server SDK with an API key show up here.
        </EmptyDescription>
      </EmptyHeader>
    </Empty>
  )
}
