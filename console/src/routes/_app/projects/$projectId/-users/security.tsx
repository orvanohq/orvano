import type { ColumnDef } from '@tanstack/react-table'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DataTable } from '@/components/ui/data-table'
import { formatDateTime } from '@/lib/format'
import type { MfaStatus, Passkey, User } from '@orvano/console-client'

import { MfaBadge } from './parts'

/**
 * A user's Security section (spec 0013, AC-44): whether MFA is on, when it was turned on and how
 * many recovery codes are left (from `consoleUsers.getMfa`), Reset MFA behind a confirmation
 * that says every session ends, and their passkeys (name, when added and last used in the viewer's
 * local time, synced or device bound, and whether it can sign in now), each with Remove. Owners
 * and developers act; viewers see the same buttons with `actionReason`.
 */
export function UserSecurity({
  user,
  mfa,
  passkeys,
  loading,
  error,
  onRetry,
  actionReason,
  onReset,
  onRemove,
}: {
  user: User
  /** The user's MFA state; undefined while it loads. */
  mfa: MfaStatus | undefined
  passkeys: readonly Passkey[]
  loading: boolean
  error: unknown
  onRetry: () => void
  /** Why the buttons are off (a viewer); undefined when they are on. */
  actionReason: string | undefined
  onReset: () => Promise<void>
  onRemove: (passkey: Passkey) => Promise<void>
}) {
  const label = user.email ?? user.id
  const columns: ColumnDef<Passkey>[] = [
    { accessorKey: 'name', header: 'Name', cell: ({ row }) => row.original.name },
    {
      accessorKey: 'createdAt',
      header: 'Added',
      cell: ({ row }) => formatDateTime(row.original.createdAt),
    },
    {
      accessorKey: 'lastUsedAt',
      header: 'Last used',
      cell: ({ row }) =>
        row.original.lastUsedAt === null ? 'Never' : formatDateTime(row.original.lastUsedAt),
    },
    {
      accessorKey: 'synced',
      header: 'Kind',
      cell: ({ row }) => (row.original.synced ? 'Synced' : 'Device bound'),
    },
    {
      accessorKey: 'active',
      header: 'State',
      cell: ({ row }) => (
        <Badge variant="status" tone={row.original.active ? 'success' : 'neutral'}>
          {row.original.active ? 'Active' : 'Inactive'}
        </Badge>
      ),
    },
    {
      id: 'remove',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => (
        <ConfirmDialog
          trigger={
            <Button
              variant="outline"
              size="sm"
              disabledReason={actionReason}
              aria-label={`Remove passkey ${row.original.name}`}
            >
              Remove
            </Button>
          }
          title={`Remove ${row.original.name}?`}
          description="The user can't sign in with this passkey anymore. Their sessions stay."
          confirmLabel="Remove passkey"
          destructive
          onConfirm={() => onRemove(row.original)}
        />
      ),
    },
  ]

  return (
    <section aria-labelledby="security-heading" className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-3">
        <h2 id="security-heading" className="text-lg font-semibold">
          Security
        </h2>
        <ConfirmDialog
          trigger={
            <Button variant="outline" size="sm" className="ml-auto" disabledReason={actionReason}>
              Reset MFA
            </Button>
          }
          title={`Reset MFA for ${label}?`}
          description="Their authenticator app and recovery codes are deleted, and every session of theirs ends, so they sign in again with one factor. Their passkeys stay."
          confirmLabel="Reset MFA"
          destructive
          onConfirm={onReset}
        />
      </div>
      <dl className="grid max-w-xl grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
        <dt className="text-muted-foreground">MFA</dt>
        <dd className="flex flex-wrap items-center gap-2">
          <MfaBadge enabled={mfa?.mfaEnabled ?? user.mfaEnabled} />
          {mfa?.totpConfirmed === true && !mfa.mfaEnabled && (
            <span className="text-muted-foreground">
              (authenticator app set up, project has TOTP off)
            </span>
          )}
        </dd>
        {mfa?.totpConfirmed === true && (
          <>
            <dt className="text-muted-foreground">Turned on</dt>
            <dd>
              {mfa.totpConfirmedAt === null ? 'Unknown' : formatDateTime(mfa.totpConfirmedAt)}
            </dd>
            <dt className="text-muted-foreground">Recovery codes left</dt>
            <dd>{mfa.recoveryCodesRemaining} of 10</dd>
          </>
        )}
      </dl>
      <h3 className="text-h3">Passkeys</h3>
      <DataTable
        label="Passkeys"
        columns={columns}
        data={passkeys}
        loading={loading}
        error={error}
        onRetry={onRetry}
        empty={<p className="p-4 text-muted-foreground">No passkey yet.</p>}
      />
    </section>
  )
}
