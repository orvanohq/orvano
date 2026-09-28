import type { QueryClient } from '@tanstack/react-query'

import { projectClient } from '@/lib/console-client'
import { keys } from '@/lib/queries'
import type { Project } from '@orvano/console-client'

/**
 * Writes a project the API returned into its query and marks its org's project list stale, so no
 * screen shows an old name or status after an action (spec 0007, key invariants).
 */
export async function applyProject(queryClient: QueryClient, project: Project): Promise<void> {
  queryClient.setQueryData(keys.project(project.id), project)
  await queryClient.invalidateQueries({ queryKey: keys.orgProjects(project.orgId) })
}

/** The project lifecycle calls the console makes from a status panel or a toast (AC-8, AC-9). */
export type ProjectAction = 'retry-setup' | 'restore' | 'retry-purge'

/**
 * Runs one lifecycle call on a project and applies the result. It does not need the project's page
 * to be mounted, so the "Project deleted" toast can restore it from anywhere (AC-8).
 */
export async function runProjectAction(
  queryClient: QueryClient,
  projectId: string,
  action: ProjectAction,
): Promise<Project> {
  const projects = projectClient(projectId).consoleProjects
  const project = await (action === 'retry-setup'
    ? projects.retryProvisioning()
    : action === 'restore'
      ? projects.restore()
      : projects.retryPurge())
  await applyProject(queryClient, project)
  return project
}
