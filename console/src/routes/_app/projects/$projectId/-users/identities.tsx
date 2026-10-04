import type { ColumnDef } from '@tanstack/react-table'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DataTable } from '@/components/ui/data-table'
import { ProviderMark, providerInfo } from '@/auth/providers'
import { formatDateTime } from '@/lib/format'
import type { Identity } from '@orvano/console-client'

import { VerifiedBadge } from './email-parts'

/**
 * A user's identities (spec 0012, AC-26): the provider, the email the provider gave and whether it
 * vouches for it, and when it was linked and last used, in the viewer's local time. Owners and
 * developers unlink, behind a confirmation; the user's last way in can't be removed.
 */
export function IdentitiesTable({
  identities,
  loading,
  error,
  onRetry,
  unlinkReason,
  onUnlink,
}: {
  identities: Identity[]
  loading: boolean
  error: unknown
  onRetry: () => void
  /** Why Unlink is off (a viewer); undefined when it is on. */
  unlinkReason: string | undefined
  onUnlink: (identity: Identity) => Promise<void>
}) {
  const columns: ColumnDef<Identity>[] = [
    {
      accessorKey: 'provider',
      header: 'Provider',
      cell: ({ row }) => (
        <span className="flex items-center gap-2">
          <ProviderMark provider={row.original.provider} />
          <span aria-hidden>{providerInfo(row.original.provider).label}</span>
        </span>
      ),
    },
    {
      accessorKey: 'email',
      header: 'Email at the provider',
      cell: ({ row }) => row.original.email ?? 'None',
    },
    {
      accessorKey: 'emailVerified',
      header: 'Verified',
      cell: ({ row }) => <VerifiedBadge verified={row.original.emailVerified} />,
    },
    {
      accessorKey: 'createdAt',
      header: 'Linked',
      cell: ({ row }) => formatDateTime(row.original.createdAt),
    },
    {
      accessorKey: 'lastSignInAt',
      header: 'Last used',
      cell: ({ row }) =>
        row.original.lastSignInAt === null ? 'Never' : formatDateTime(row.original.lastSignInAt),
    },
    {
      id: 'unlink',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const label = providerInfo(row.original.provider).label
        return (
          <ConfirmDialog
            trigger={
              <Button
                variant="outline"
                size="sm"
                disabledReason={unlinkReason}
                aria-label={`Unlink ${label}`}
              >
                Unlink
              </Button>
            }
            title={`Unlink ${label}?`}
            description={`The user can't sign in with ${label} anymore. Their sessions stay.`}
            confirmLabel="Unlink"
            destructive
            onConfirm={() => onUnlink(row.original)}
          />
        )
      },
    },
  ]

  return (
    <DataTable
      label="Identities"
      columns={columns}
      data={identities}
      loading={loading}
      error={error}
      onRetry={onRetry}
      empty={<p className="p-4 text-muted-foreground">No provider is linked.</p>}
    />
  )
}
