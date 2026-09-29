import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { Users } from 'lucide-react'
import { useCallback, useMemo, useState } from 'react'

import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { DataTable } from '@/components/ui/data-table'
import { Empty, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { consoleApi } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import {
  accountQuery,
  invalidateOrgLists,
  keys,
  membersQuery,
  orgQuery,
  orgsQuery,
} from '@/lib/queries'
import { roleReason } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { PageHeading } from '@/shell/page-heading'
import type { Invitation, Member, OrgRole } from '@orvano/console-client'

import { ChangeRoleDialog } from './-members/change-role-dialog'
import { memberColumns, restoreFirst, type MemberAction } from './-members/columns'
import { useMembershipFailure } from './-members/failures'
import { InviteDialog } from './-members/invite-dialog'
import { memberName } from './-members/member-name'
import { PendingInvitations } from './-members/pending-invitations'

export const Route = createFileRoute('/_app/orgs/$orgId/members')({
  component: MembersPage,
})

/**
 * The org's members (spec 0008, AC-15 to AC-19): every member sees who else is in the org and can
 * leave; owners invite, resend and revoke invitations, change roles, and remove members. The API's
 * 403 stays the real guard; the page only hides or disables what your role can't do.
 */
function MembersPage() {
  const { orgId } = Route.useParams()
  const org = useQuery(orgQuery(orgId)).data
  usePageTitle('Members', org?.name)
  const role = org?.role
  const deleting = org?.status === 'deleting'
  const accountId = useQuery(accountQuery()).data?.id
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const failed = useMembershipFailure(orgId)
  const members = useInfiniteQuery(membersQuery(orgId))
  const rows = members.data?.pages.flatMap((page) => page.items) ?? []

  const [inviting, setInviting] = useState(false)
  const [resending, setResending] = useState<Invitation | null>(null)
  const [acting, setActing] = useState<{ member: Member; action: MemberAction } | null>(null)

  const refreshMembers = useCallback(
    () => queryClient.invalidateQueries({ queryKey: keys.members(orgId) }),
    [queryClient, orgId],
  )
  const refreshInvitations = useCallback(
    () => queryClient.invalidateQueries({ queryKey: keys.invitations(orgId) }),
    [queryClient, orgId],
  )

  const columns = useMemo(
    () =>
      memberColumns({
        role,
        accountId,
        deleting,
        onAction: (member, action) => {
          setActing({ member, action })
        },
      }),
    [role, accountId, deleting],
  )

  /** Runs a member change; a 404 or `org_not_active` closes the dialog with a toast, the rest stay in it. */
  const change = async (title: string, work: () => Promise<void>) => {
    try {
      await work()
    } catch (error) {
      if (!failed(title, error)) throw error
    }
  }

  const changeRole = (member: Member, next: OrgRole) =>
    change("Couldn't change the role", async () => {
      await consoleApi().consoleMembers.update(orgId, member.userId, { role: next })
      await refreshMembers()
      // Your own role moves the owner only actions at once (AC-19).
      if (member.userId === accountId) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: keys.org(orgId), exact: true }),
          invalidateOrgLists(queryClient),
        ])
      }
      notifySuccess('Role changed', `${memberName(member)} is now ${next}.`)
    })

  const remove = (member: Member) =>
    change("Couldn't remove the member", async () => {
      await consoleApi().consoleMembers.remove(orgId, member.userId)
      await refreshMembers()
      notifySuccess('Member removed', `${memberName(member)} no longer has access to this org.`)
    })

  const leave = () =>
    change("Couldn't leave the org", async () => {
      if (accountId === undefined) return
      await consoleApi().consoleMembers.remove(orgId, accountId)
      notifySuccess(`You left ${org?.name ?? 'the org'}`)
      await invalidateOrgLists(queryClient)
      const orgs = await queryClient.infiniteQuery(orgsQuery())
      const next = orgs.pages.flatMap((page) => page.items).find((item) => item.id !== orgId)
      await (next === undefined
        ? navigate({ to: '/orgs', replace: true })
        : navigate({ to: '/orgs/$orgId', params: { orgId: next.id }, replace: true }))
      // This org's pages are gone for you; drop them so nothing asks for them again.
      queryClient.removeQueries({ queryKey: keys.org(orgId) })
    })

  const revoke = useCallback(
    async (invitation: Invitation) => {
      try {
        await consoleApi().consoleInvitations.revoke(orgId, invitation.id)
      } catch (error) {
        if (failed("Couldn't revoke the invite", error)) return
        throw error
      }
      await refreshInvitations()
      notifySuccess('Invite revoked', `The link for ${invitation.email} no longer works.`)
    },
    [orgId, failed, refreshInvitations],
  )

  const inviteReason = deleting
    ? restoreFirst
    : role === 'owner'
      ? undefined
      : role === undefined
        ? 'Loading'
        : roleReason('owner')

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>Members</PageHeading>
        <div data-slot="page-actions" className="ml-auto">
          <Button
            disabledReason={inviteReason}
            onClick={() => {
              setInviting(true)
            }}
          >
            Invite
          </Button>
        </div>
      </div>
      <InviteDialog
        open={inviting}
        onOpenChange={setInviting}
        createInvitation={(body) => consoleApi().consoleInvitations.create(orgId, body)}
        onCreated={() => {
          void refreshInvitations()
        }}
      />
      {resending === null ? null : (
        <InviteDialog
          key={resending.id}
          open
          onOpenChange={(open) => {
            if (!open) setResending(null)
          }}
          resend={{ email: resending.email, role: resending.role }}
          createInvitation={(body) => consoleApi().consoleInvitations.create(orgId, body)}
          onCreated={() => {
            void refreshInvitations()
          }}
        />
      )}
      <DataTable
        label="Members"
        columns={columns}
        getRowId={(member) => member.userId}
        data={rows}
        loading={members.isPending}
        error={members.isError ? members.error : undefined}
        onRetry={() => {
          void members.refetch()
        }}
        hasMore={members.hasNextPage}
        loadingMore={members.isFetchingNextPage}
        onLoadMore={() => {
          void members.fetchNextPage()
        }}
        // A page can come back short or empty while more remain (AC-8): then "Load more" shows instead.
        empty={
          members.hasNextPage ? undefined : (
            <Empty className="border">
              <EmptyHeader>
                <EmptyMedia variant="icon">
                  <Users aria-hidden />
                </EmptyMedia>
                <EmptyTitle>No members to show</EmptyTitle>
              </EmptyHeader>
            </Empty>
          )
        }
      />
      {role === 'owner' ? (
        <PendingInvitations
          orgId={orgId}
          deleting={deleting}
          onResend={setResending}
          onRevoke={revoke}
        />
      ) : null}
      {acting?.action === 'role' ? (
        <ChangeRoleDialog
          key={acting.member.userId}
          member={acting.member}
          isSelf={acting.member.userId === accountId}
          open
          onOpenChange={(open) => {
            if (!open) setActing(null)
          }}
          onSave={(next) => changeRole(acting.member, next)}
        />
      ) : null}
      {acting?.action === 'remove' ? (
        <ConfirmDialog
          key={acting.member.userId}
          open
          onOpenChange={(open) => {
            if (!open) setActing(null)
          }}
          title={`Remove ${memberName(acting.member)} from ${org?.name ?? 'this org'}?`}
          description="Their access to the org and its projects ends at once. API keys they created keep working."
          confirmLabel="Remove member"
          destructive
          onConfirm={() => remove(acting.member)}
        />
      ) : null}
      {acting?.action === 'leave' ? (
        <ConfirmDialog
          open
          onOpenChange={(open) => {
            if (!open) setActing(null)
          }}
          title={`Leave ${org?.name ?? 'this org'}?`}
          description="You lose access to the org and its projects at once. An owner can invite you again."
          confirmLabel="Leave org"
          destructive
          onConfirm={leave}
        />
      ) : null}
    </div>
  )
}
