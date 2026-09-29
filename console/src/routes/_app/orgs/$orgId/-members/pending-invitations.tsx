import { useInfiniteQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { MailPlus } from 'lucide-react'
import { useMemo } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DataTable } from '@/components/ui/data-table'
import { Empty, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { formatDate, formatFull } from '@/lib/format'
import { invitationsQuery } from '@/lib/queries'
import { roleInfo } from '@/lib/roles'
import type { Invitation } from '@orvano/console-client'

import { restoreFirst } from './columns'
import { userRefName } from './member-name'

/** When an invitation stops working: its date, or an "Expired" badge (spec 0008, AC-18). */
function InvitationExpiry({ invitation }: { invitation: Invitation }) {
  if (invitation.status === 'expired') {
    return (
      <Badge variant="status" tone="danger">
        Expired
      </Badge>
    )
  }
  return (
    <time dateTime={invitation.expiresAt} title={formatFull(invitation.expiresAt)}>
      {formatDate(invitation.expiresAt)}
    </time>
  )
}

/**
 * The owners' Pending invitations (spec 0008, AC-18): Email, Role, Invited by, Expires, and Resend
 * and Revoke. Developers and viewers never render it, so they never fetch the list.
 */
export function PendingInvitations({
  orgId,
  deleting,
  onResend,
  onRevoke,
}: {
  orgId: string
  deleting: boolean
  /** Opens the invite dialog's link step with a new link for this invitation's email and role. */
  onResend: (invitation: Invitation) => void
  /** Revokes it; throw to keep the confirm open with the error. */
  onRevoke: (invitation: Invitation) => Promise<void>
}) {
  const invitations = useInfiniteQuery(invitationsQuery(orgId))
  const rows = invitations.data?.pages.flatMap((page) => page.items) ?? []
  const columns = useMemo<ColumnDef<Invitation>[]>(
    () => [
      {
        accessorKey: 'email',
        header: 'Email',
        cell: ({ row }) => <span className="font-medium">{row.original.email}</span>,
      },
      {
        accessorKey: 'role',
        header: 'Role',
        cell: ({ row }) => <Badge>{roleInfo[row.original.role].label}</Badge>,
      },
      {
        id: 'invitedBy',
        accessorFn: (invitation) => userRefName(invitation.invitedBy),
        header: 'Invited by',
      },
      {
        accessorKey: 'expiresAt',
        header: 'Expires',
        cell: ({ row }) => <InvitationExpiry invitation={row.original} />,
      },
      {
        id: 'actions',
        header: () => <span className="sr-only">Actions</span>,
        enableSorting: false,
        cell: ({ row }) => {
          const invitation = row.original
          const reason = deleting ? restoreFirst : undefined
          return (
            <span className="flex justify-end gap-2">
              <Button
                variant="outline"
                size="sm"
                aria-label={`Resend the invite to ${invitation.email}`}
                disabledReason={reason}
                onClick={() => {
                  onResend(invitation)
                }}
              >
                Resend
              </Button>
              <ConfirmDialog
                trigger={
                  <Button
                    variant="outline"
                    size="sm"
                    aria-label={`Revoke the invite to ${invitation.email}`}
                    disabledReason={reason}
                  >
                    Revoke
                  </Button>
                }
                title={`Revoke the invite to ${invitation.email}?`}
                description="Its link stops working at once. You can invite them again later."
                confirmLabel="Revoke invite"
                destructive
                onConfirm={() => onRevoke(invitation)}
              />
            </span>
          )
        },
      },
    ],
    [deleting, onResend, onRevoke],
  )

  return (
    // Not a landmark of its own: the table's scroll region already carries the name.
    <section className="flex flex-col gap-3">
      <h2 className="text-lg/7 font-semibold">Pending invitations</h2>
      <DataTable
        label="Pending invitations"
        columns={columns}
        data={rows}
        loading={invitations.isPending}
        error={invitations.isError ? invitations.error : undefined}
        onRetry={() => {
          void invitations.refetch()
        }}
        hasMore={invitations.hasNextPage}
        loadingMore={invitations.isFetchingNextPage}
        onLoadMore={() => {
          void invitations.fetchNextPage()
        }}
        empty={
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <MailPlus aria-hidden />
              </EmptyMedia>
              <EmptyTitle>No pending invitations</EmptyTitle>
            </EmptyHeader>
          </Empty>
        }
      />
    </section>
  )
}
