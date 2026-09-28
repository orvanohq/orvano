import type { ColumnDef } from '@tanstack/react-table'
import { KeyRound } from 'lucide-react'
import type { ReactNode } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { formatDate, formatFull } from '@/lib/format'
import { roleReason } from '@/lib/roles'
import { RelativeTime } from '@/shell/relative-time'
import type { ApiKey, OrgRole } from '@orvano/console-client'

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

/**
 * Why you can't delete `key`, or undefined when you can (spec 0007, AC-16): owners delete any key,
 * developers only the keys they created, viewers none. The API's 403 stays the real guard.
 */
export function deleteKeyReason(
  key: ApiKey,
  role: OrgRole | undefined,
  accountId: string | undefined,
): string | undefined {
  if (role === 'owner') return undefined
  if (role !== 'developer') return roleReason('developer')
  return key.createdByUserId === accountId ? undefined : 'You can delete only keys you created'
}

/** Who created a key: "You" or "Teammate", and a skeleton until your account has loaded (AC-12). */
export function CreatedBy({
  userId,
  accountId,
}: {
  userId: string
  accountId: string | undefined
}) {
  if (accountId === undefined) {
    return (
      <>
        <Skeleton aria-hidden className="h-4 w-16" />
        <span className="sr-only">Loading</span>
      </>
    )
  }
  return userId === accountId ? 'You' : 'Teammate'
}

/**
 * The API keys table (AC-12 and AC-16): the secret is never here, only its 12 character prefix.
 * `accountId` is your console account's ID (undefined while it loads).
 */
export function keyColumns({
  role,
  accountId,
  onDelete,
}: {
  role: OrgRole | undefined
  accountId: string | undefined
  onDelete: (key: ApiKey) => Promise<void>
}): ColumnDef<ApiKey>[] {
  return [
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
    {
      accessorKey: 'createdByUserId',
      header: 'Created by',
      cell: ({ row }) => <CreatedBy userId={row.original.createdByUserId} accountId={accountId} />,
    },
    {
      id: 'delete',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      cell: ({ row }) => {
        const key = row.original
        return (
          <ConfirmDialog
            trigger={
              <Button
                variant="outline"
                size="sm"
                aria-label={`Delete ${key.name}`}
                disabledReason={deleteKeyReason(key, role, accountId)}
              >
                Delete
              </Button>
            }
            title={`Delete the key ${key.name}?`}
            description={`Anything using the key starting ${key.prefix}… stops working at once. This can't be undone.`}
            confirmLabel="Delete key"
            destructive
            onConfirm={() => onDelete(key)}
          />
        )
      },
    },
  ]
}

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
