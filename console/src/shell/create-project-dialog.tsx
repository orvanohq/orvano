import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import type { RefObject } from 'react'

import { consoleApi } from '@/lib/console-client'
import { keys } from '@/lib/queries'
import { roleReason } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { NameDialog } from '@/shell/name-dialog'
import { meetsRole } from '@/shell/nav'
import type { Org } from '@orvano/console-client'

/**
 * Why you can't create a project in this org, or undefined when you can (spec 0007, AC-6): viewers
 * can't, and nobody can while the org is being deleted. Undefined `org` means it is still loading.
 */
export function createProjectReason(org: Org | undefined): string | undefined {
  if (!meetsRole(org?.role, 'developer')) return roleReason('developer')
  if (org?.status === 'deleting') return 'Restore the org first'
  return undefined
}

/**
 * Create project (spec 0007, AC-6), opened from the project switcher's footer and the org page. On
 * success the org's project list refreshes and you land on the new project, which shows "Setting
 * up" until it is active.
 */
export function CreateProjectDialog({
  orgId,
  open,
  onOpenChange,
  finalFocus,
}: {
  orgId: string
  open: boolean
  onOpenChange: (open: boolean) => void
  finalFocus?: RefObject<HTMLElement | null> | undefined
}) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  return (
    <NameDialog
      id="create-project"
      open={open}
      onOpenChange={onOpenChange}
      finalFocus={finalFocus}
      title="Create a project"
      description="A project is one app's backend: its users, data, and keys, kept apart from other projects."
      submitLabel="Create project"
      errorTitle="Couldn't create the project"
      onSubmit={async (name) => {
        const project = await consoleApi().consoleProjects.create(orgId, { name })
        queryClient.setQueryData(keys.project(project.id), project)
        await queryClient.invalidateQueries({ queryKey: keys.orgProjects(orgId) })
        notifySuccess('Project created', project.name)
        await navigate({ to: '/projects/$projectId', params: { projectId: project.id } })
      }}
    />
  )
}
