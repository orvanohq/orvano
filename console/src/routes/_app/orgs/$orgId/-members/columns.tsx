import type { ColumnDef } from '@tanstack/react-table'
import { Ellipsis } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { roleInfo } from '@/lib/roles'
import { RelativeTime } from '@/shell/relative-time'
import type { Member, OrgRole } from '@orvano/console-client'

import { memberName } from './member-name'

/** What a row menu item asks the page to open. */
export type MemberAction = 'role' | 'remove' | 'leave'

/** The reason every membership change is off while the org is being deleted (spec 0008, AC-19). */
export const restoreFirst = 'Restore the org first'

/**
 * Which items a member's row menu holds (AC-19): owners get Change role and Remove on every other
 * member's row; your own row holds Change role (owners only) and Leave org. Nothing for a developer
 * or viewer on someone else's row.
 */
export function memberActions(
  member: Member,
  role: OrgRole | undefined,
  accountId: string | undefined,
): MemberAction[] {
  const isSelf = member.userId === accountId
  const owner = role === 'owner'
  if (isSelf) return owner ? ['role', 'leave'] : ['leave']
  return owner ? ['role', 'remove'] : []
}

const actionLabels: Record<MemberAction, string> = {
  role: 'Change role',
  remove: 'Remove',
  leave: 'Leave org',
}

/** A member's row menu; each item is off with its reason while the org is being deleted. */
function RowMenu({
  member,
  actions,
  deleting,
  onAction,
}: {
  member: Member
  actions: MemberAction[]
  deleting: boolean
  onAction: (member: Member, action: MemberAction) => void
}) {
  if (actions.length === 0) return null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="ghost" size="icon" aria-label={`Actions for ${memberName(member)}`} />
        }
      >
        <Ellipsis aria-hidden />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-52">
        {actions.map((action) => (
          <DropdownMenuItem
            key={action}
            disabled={deleting}
            variant={action === 'role' ? 'default' : 'destructive'}
            onClick={() => {
              onAction(member, action)
            }}
          >
            <span className="flex flex-col">
              {actionLabels[action]}
              {deleting ? (
                <span className="text-small text-muted-foreground">{restoreFirst}</span>
              ) : null}
            </span>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

/**
 * The members table (spec 0008, AC-15): Name with a "You" badge on your row, Email, Role, Status (a
 * "Blocked" badge only when blocked), Joined, and the row menu. `accountId` is your console
 * account's ID (undefined while it loads).
 */
export function memberColumns({
  role,
  accountId,
  deleting,
  onAction,
}: {
  role: OrgRole | undefined
  accountId: string | undefined
  deleting: boolean
  onAction: (member: Member, action: MemberAction) => void
}): ColumnDef<Member>[] {
  return [
    {
      id: 'name',
      accessorFn: (member) => memberName(member),
      header: 'Name',
      cell: ({ row }) => (
        <span className="flex items-center gap-2">
          <span className="font-medium">{memberName(row.original)}</span>
          {row.original.userId === accountId ? <Badge variant="primary">You</Badge> : null}
        </span>
      ),
    },
    { accessorKey: 'email', header: 'Email' },
    {
      accessorKey: 'role',
      header: 'Role',
      cell: ({ row }) => <Badge>{roleInfo[row.original.role].label}</Badge>,
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) =>
        row.original.status === 'blocked' ? (
          <Badge variant="status" tone="danger">
            Blocked
          </Badge>
        ) : null,
    },
    {
      accessorKey: 'joinedAt',
      header: 'Joined',
      cell: ({ row }) => <RelativeTime iso={row.original.joinedAt} />,
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      enableSorting: false,
      cell: ({ row }) => (
        <RowMenu
          member={row.original}
          actions={memberActions(row.original, role, accountId)}
          deleting={deleting}
          onAction={onAction}
        />
      ),
    },
  ]
}
