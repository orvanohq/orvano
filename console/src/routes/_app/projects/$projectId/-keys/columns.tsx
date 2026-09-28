import type { ColumnDef } from '@tanstack/react-table'
import { KeyRound } from 'lucide-react'
import type { ReactNode } from 'react'

import { Badge } from '@/components/ui/badge'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { formatDate, formatFull } from '@/lib/format'
import { RelativeTime } from '@/shell/relative-time'
import type { ApiKey } from '@orvano/console-client'

import { isExpired } from './expiry'

/** When a key stops working: "Never", its date, or an "Expired" badge (spec 0007, AC-12). */
export function KeyExpiry({ expiresAt }: { expiresAt: string | null }) {
  if (expiresAt === null) return 'Never'
  if (isExpired(expiresAt)) {
    return (
      <Badge variant="status" tone="danger">
        Expired
      </Badge>
    )
  }
  return (
    <time dateTime={expiresAt} title={formatFull(expiresAt)}>
      {formatDate(expiresAt)}
    </time>
  )
}

/** The API keys table (AC-12): the secret is never here, only its 12 character prefix. */
export const keyColumns: ColumnDef<ApiKey>[] = [
  {
    accessorKey: 'name',
    header: 'Name',
    cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
  },
  {
    accessorKey: 'prefix',
    header: 'Key',
    cell: ({ row }) => <code className="font-mono text-mono">{row.original.prefix}…</code>,
  },
  {
    accessorKey: 'scopes',
    header: 'Scopes',
    cell: ({ row }) => (
      <span className="flex flex-wrap gap-1">
        {row.original.scopes.map((scope) => (
          <Badge key={scope} className="font-mono">
            {scope}
          </Badge>
        ))}
      </span>
    ),
  },
  {
    accessorKey: 'expiresAt',
    header: 'Expires',
    cell: ({ row }) => <KeyExpiry expiresAt={row.original.expiresAt} />,
  },
  {
    accessorKey: 'lastUsedAt',
    header: 'Last used',
    cell: ({ row }) =>
      row.original.lastUsedAt === null ? 'Never' : <RelativeTime iso={row.original.lastUsedAt} />,
  },
  {
    accessorKey: 'createdAt',
    header: 'Created',
    cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
  },
]

/** The empty keys table, with "Create key" (AC-12). */
export function NoKeys({ action }: { action: ReactNode }) {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <KeyRound aria-hidden />
        </EmptyMedia>
        <EmptyTitle>No API keys yet</EmptyTitle>
        <EmptyDescription>
          Your server code uses an API key to call this project, with only the scopes you give it.
        </EmptyDescription>
      </EmptyHeader>
      <div data-slot="empty-action">{action}</div>
    </Empty>
  )
}
