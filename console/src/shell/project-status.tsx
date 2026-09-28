import { useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, Loader2, Trash2 } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { formatFull } from '@/lib/format'
import { usePageTitle } from '@/lib/page-title'
import { runProjectAction, type ProjectAction } from '@/lib/project-actions'
import { orgQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { useStateMoved } from '@/lib/state-moved'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import type { OrgRole, OrgStatus, Project } from '@orvano/console-client'

/** How long "Setting up" runs before it adds the "taking longer than usual" note. */
const slowAfterMs = 60_000

/** What the status panels' buttons need: your role, the org's status, and a way to run an action. */
export interface StatusActions {
  role: OrgRole | undefined
  orgStatus: OrgStatus | undefined
  /** Runs the action; resolves when it is done, whether it worked or not (it reports its own errors). */
  run: (action: ProjectAction) => Promise<void>
}

const actionText: Record<ProjectAction, { label: string; done: string; failed: string }> = {
  'retry-setup': {
    label: 'Retry setup',
    done: 'Setup restarted',
    failed: "Couldn't retry the setup",
  },
  restore: {
    label: 'Restore project',
    done: 'Project restored',
    failed: "Couldn't restore the project",
  },
  'retry-purge': {
    label: 'Retry purge',
    done: 'Purge restarted',
    failed: "Couldn't retry the purge",
  },
}

/**
 * The status panels' actions for `project` (spec 0007, AC-9): each call writes the returned project
 * into its query, so the page updates in place, and confirms with a toast. When the state moved
 * under you the error toast is followed by a refetch (AC-10).
 */
export function useStatusActions(project: Project): StatusActions {
  const queryClient = useQueryClient()
  const role = useOrgRole()
  const org = useQuery(orgQuery(project.orgId)).data
  const stateMoved = useStateMoved()
  return {
    role,
    orgStatus: org?.status,
    run: async (action) => {
      const text = actionText[action]
      try {
        await runProjectAction(queryClient, project.id, action)
        notifySuccess(text.done, project.name)
      } catch (error) {
        const target = { projectId: project.id, orgId: project.orgId }
        if (!stateMoved(text.failed, error, target)) notifyError(text.failed, error)
      }
    },
  }
}

/**
 * What a project page shows instead of its product pages while the project is not `active` (AC-18).
 * With `actions` the panels offer their recovery buttons (spec 0007, AC-9): Retry setup when setup
 * failed, Restore project and, after a failed purge, Retry purge while it is being deleted.
 */
export function ProjectStatusPanel({
  project,
  actions,
}: {
  project: Project
  actions?: StatusActions | undefined
}) {
  switch (project.status) {
    case 'provisioning':
      return <SettingUp project={project} />
    case 'failed':
      return (
        <Panel
          icon={<CircleAlert aria-hidden className="text-danger-text" />}
          project={project}
          title="Setup failed"
          action={
            actions === undefined ? null : (
              <ActionButton
                action="retry-setup"
                actions={actions}
                disabledReason={
                  meetsRole(actions.role, 'developer') ? undefined : roleReason('developer')
                }
              />
            )
          }
        >
          <p>Something went wrong while setting this project up.</p>
        </Panel>
      )
    case 'deleting':
      return (
        <Panel
          icon={<Trash2 aria-hidden />}
          project={project}
          title="Being deleted"
          action={
            actions === undefined ? null : (
              <>
                <ActionButton
                  action="restore"
                  actions={actions}
                  disabledReason={
                    !meetsRole(actions.role, 'owner')
                      ? roleReason('owner')
                      : actions.orgStatus === 'deleting'
                        ? 'Restore the org first'
                        : undefined
                  }
                />
                {project.purgeFailedAt === null ? null : (
                  <ActionButton
                    action="retry-purge"
                    variant="outline"
                    actions={actions}
                    disabledReason={
                      meetsRole(actions.role, 'owner') ? undefined : roleReason('owner')
                    }
                  />
                )}
              </>
            )
          }
        >
          <p>
            This project is being deleted
            {project.purgeAfter === null
              ? '.'
              : ` and is purged for good on ${formatFull(project.purgeAfter)}.`}
          </p>
          {project.purgeFailedAt === null ? null : (
            <p className="text-danger-text">
              The last purge attempt failed on {formatFull(project.purgeFailedAt)}.
            </p>
          )}
        </Panel>
      )
    case 'active':
      return null
  }
}

function SettingUp({ project }: { project: Project }) {
  const [slow, setSlow] = useState(false)
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSlow(true)
    }, slowAfterMs)
    return () => {
      window.clearTimeout(timer)
    }
  }, [])
  return (
    <Panel
      icon={<Loader2 aria-hidden className="animate-spin" />}
      project={project}
      title="Setting up"
      live
    >
      <p>Your project is being set up. This page updates when it is ready.</p>
      {slow ? <p className="text-warning-text">This is taking longer than usual.</p> : null}
    </Panel>
  )
}

/**
 * One recovery button: runs at once with no confirm, shows a spinner and `aria-busy` while pending,
 * and ignores clicks meanwhile (AC-9, AC-10). Focus moves to the page heading afterwards, since the
 * button usually leaves with the panel it was on.
 */
function ActionButton({
  action,
  actions,
  disabledReason,
  variant = 'primary',
}: {
  action: ProjectAction
  actions: StatusActions
  disabledReason: string | undefined
  variant?: 'primary' | 'outline'
}) {
  const [pending, setPending] = useState(false)
  return (
    <Button
      variant={variant}
      loading={pending}
      disabledReason={disabledReason}
      onClick={() => {
        setPending(true)
        void actions.run(action).finally(() => {
          setPending(false)
          if (document.activeElement === document.body) {
            document.getElementById('page-title')?.focus()
          }
        })
      }}
    >
      {actionText[action].label}
    </Button>
  )
}

function Panel({
  icon,
  title,
  project,
  live = false,
  action = null,
  children,
}: {
  icon: ReactNode
  title: string
  project: Project
  live?: boolean
  action?: ReactNode
  children: ReactNode
}) {
  usePageTitle(title, project.name)
  return (
    <div className="mx-auto flex max-w-xl flex-col gap-3 rounded-lg border border-border bg-card p-6">
      <div className="flex items-center gap-2 text-muted-foreground">
        {icon}
        <span>{project.name}</span>
      </div>
      <PageHeading>{title}</PageHeading>
      <div role={live ? 'status' : undefined} className="flex flex-col gap-2 text-body">
        {children}
      </div>
      <div data-slot="status-action" className="flex flex-wrap gap-2 empty:hidden">
        {action}
      </div>
    </div>
  )
}
