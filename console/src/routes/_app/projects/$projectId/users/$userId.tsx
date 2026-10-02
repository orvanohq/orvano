import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, createFileRoute, notFound, useNavigate } from '@tanstack/react-router'

import { Button } from '@/components/ui/button'
import { CopyableId } from '@/components/ui/code-block'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { projectClient } from '@/lib/console-client'
import { isNotFound } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import { keys, platformsQuery, userQuery, userSessionsQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifyError, notifySuccess } from '@/lib/toast'
import { InShellNotFound } from '@/shell/in-shell-not-found'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import { RelativeTime } from '@/shell/relative-time'
import type { User } from '@orvano/console-client'

import { EmailCard } from '../-users/email-parts'
import { SessionsTable, UserStatusBadge } from '../-users/parts'

export const Route = createFileRoute('/_app/projects/$projectId/users/$userId')({
  loader: async ({ context, params }) => {
    try {
      await context.queryClient.query({
        ...userQuery(params.projectId, params.userId),
        staleTime: 'static',
      })
    } catch (error) {
      if (isNotFound(error, 'user_not_found')) throw notFound()
      throw error
    }
  },
  notFoundComponent: () => <InShellNotFound what="user" />,
  component: UserPage,
})

/**
 * One user (spec 0004, AC-29): their details, email and verification (spec 0010, AC-22, AC-23), and
 * active sessions. Owners and developers block, unblock, delete, end sessions, and run the email
 * actions; viewers see the same buttons with the reason.
 */
function UserPage() {
  const { projectId, userId } = Route.useParams()
  const user = useQuery(userQuery(projectId, userId)).data
  usePageTitle(user?.email ?? 'User', 'Users')
  const role = useOrgRole()
  const reason = meetsRole(role, 'developer') ? undefined : roleReason('developer')
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const sessions = useInfiniteQuery(userSessionsQuery(projectId, userId))
  const platforms = useInfiniteQuery(platformsQuery(projectId))
  const webHosts = (platforms.data?.pages.flatMap((page) => page.items) ?? [])
    .filter((platform) => platform.type === 'web')
    .map((platform) => platform.identifier)
  const client = projectClient(projectId)

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: keys.users(projectId) })
  }
  const run = async (title: string, work: () => Promise<unknown>) => {
    try {
      await work()
      await refresh()
      notifySuccess(title)
    } catch (error) {
      notifyError(`Couldn't ${title.toLowerCase()}`, error)
    }
  }

  if (user === undefined) return null
  const label = user.email ?? user.id

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <Link
        to="/projects/$projectId/users"
        params={{ projectId }}
        className="w-fit text-link hover:underline"
      >
        All users
      </Link>
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>{label}</PageHeading>
        <UserStatusBadge status={user.status} />
        <div className="ml-auto flex flex-wrap gap-2">
          <StatusAction user={user} reason={reason} onChange={run} projectId={projectId} />
          <ConfirmDialog
            trigger={
              <Button variant="destructive" disabledReason={reason}>
                Delete user
              </Button>
            }
            title="Delete this user?"
            description="Their account, password, and sessions are deleted now. It can't be undone."
            confirmLabel="Delete user"
            destructive
            requireName={label}
            onConfirm={async () => {
              try {
                await client.consoleUsers.delete(userId)
              } catch (error) {
                notifyError("Couldn't delete the user", error)
                return
              }
              await refresh()
              notifySuccess('User deleted', label)
              await navigate({
                to: '/projects/$projectId/users',
                params: { projectId },
                replace: true,
              })
            }}
          />
        </div>
      </div>
      <dl className="grid max-w-xl grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
        <dt className="text-muted-foreground">User ID</dt>
        <dd>
          <CopyableId value={user.id} label="user ID" />
        </dd>
        <dt className="text-muted-foreground">Name</dt>
        <dd>{user.name ?? 'None'}</dd>
        <dt className="text-muted-foreground">Signed up</dt>
        <dd>
          <RelativeTime iso={user.createdAt} />
        </dd>
        <dt className="text-muted-foreground">Last sign in</dt>
        <dd>{user.lastSignInAt === null ? 'Never' : <RelativeTime iso={user.lastSignInAt} />}</dd>
      </dl>
      <EmailCard
        user={user}
        projectId={projectId}
        webHosts={webHosts}
        disabledReason={reason}
        onSetVerified={async (verified) => {
          await client.consoleUsers.updateEmailVerification(userId, { verified })
          await refresh()
          notifySuccess(verified ? 'Marked as verified' : 'Marked as unverified', label)
        }}
        onSendVerification={async (redirectUrl) => {
          await client.consoleUsers.createVerification(userId, { redirectUrl })
          notifySuccess(`Sent to ${label}.`)
        }}
        onSendRecovery={async (redirectUrl) => {
          await client.consoleUsers.createRecovery(userId, { redirectUrl })
          notifySuccess(`Sent to ${label}.`)
        }}
        onChangeEmail={async ({ email, emailVerified }) => {
          const changed = await client.consoleUsers.updateEmail(userId, { email, emailVerified })
          await refresh()
          notifySuccess('Email changed', changed.email ?? email)
        }}
      />
      <section aria-labelledby="sessions-heading" className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 id="sessions-heading" className="text-lg font-semibold">
            Active sessions
          </h2>
          <ConfirmDialog
            trigger={
              <Button variant="outline" size="sm" className="ml-auto" disabledReason={reason}>
                End all sessions
              </Button>
            }
            title="End every session of this user?"
            description="Every device they are signed in on signs out."
            confirmLabel="End all sessions"
            destructive
            onConfirm={() =>
              run('Sessions ended', () => client.consoleUsers.deleteSessions(userId))
            }
          />
        </div>
        <SessionsTable
          sessions={sessions.data?.pages.flatMap((page) => page.items) ?? []}
          loading={sessions.isPending}
          error={sessions.isError ? sessions.error : undefined}
          onRetry={() => {
            void sessions.refetch()
          }}
          hasMore={sessions.hasNextPage}
          onLoadMore={() => {
            void sessions.fetchNextPage()
          }}
          endReason={reason}
          onEnd={(session) =>
            run('Session ended', () => client.consoleUsers.deleteSession(userId, session.id))
          }
        />
      </section>
    </div>
  )
}

/** Block, or Unblock for a blocked user; blocking ends every session, behind a confirmation. */
function StatusAction({
  user,
  reason,
  projectId,
  onChange,
}: {
  user: User
  reason: string | undefined
  projectId: string
  onChange: (title: string, work: () => Promise<unknown>) => Promise<void>
}) {
  const client = projectClient(projectId)
  if (user.status === 'blocked') {
    return (
      <Button
        variant="outline"
        disabledReason={reason}
        onClick={() => {
          void onChange('User unblocked', () => client.consoleUsers.unblock(user.id))
        }}
      >
        Unblock
      </Button>
    )
  }
  return (
    <ConfirmDialog
      trigger={
        <Button variant="outline" disabledReason={reason}>
          Block
        </Button>
      }
      title="Block this user?"
      description="Every session ends now, and they can't sign in until you unblock them."
      confirmLabel="Block user"
      destructive
      onConfirm={() => onChange('User blocked', () => client.consoleUsers.block(user.id))}
    />
  )
}
