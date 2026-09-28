import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Outlet, createFileRoute, notFound } from '@tanstack/react-router'
import { useEffect, useRef } from 'react'

import { isNotFound } from '@/lib/errors'
import { clearLastProject, setLastProject } from '@/lib/last-project'
import { keys, projectQuery } from '@/lib/queries'
import { InShellNotFound } from '@/shell/in-shell-not-found'
import { ProjectStatusPanel, useStatusActions } from '@/shell/project-status'
import type { Project } from '@orvano/console-client'

export const Route = createFileRoute('/_app/projects/$projectId')({
  loader: async ({ context, params }) => {
    try {
      await context.queryClient.query({ ...projectQuery(params.projectId), staleTime: 'static' })
      setLastProject(params.projectId)
    } catch (error) {
      if (isNotFound(error, 'project_not_found')) {
        clearLastProject(params.projectId)
        throw notFound()
      }
      throw error
    }
  },
  notFoundComponent: () => <InShellNotFound what="project" />,
  component: ProjectLayout,
})

// Product content renders only while the project is active (spec 0005, key invariants).
function ProjectLayout() {
  const { projectId } = Route.useParams()
  const queryClient = useQueryClient()
  const project = useQuery({
    ...projectQuery(projectId),
    // Check again every 2 seconds while the project is being set up.
    refetchInterval: (query) => (query.state.data?.status === 'provisioning' ? 2000 : false),
  })
  const data = project.data

  // When the status changes, the org's project list (the switcher, the org page) is out of date.
  const lastStatus = useRef(data?.status)
  useEffect(() => {
    if (data === undefined) return
    if (lastStatus.current !== undefined && lastStatus.current !== data.status) {
      void queryClient.invalidateQueries({ queryKey: keys.orgProjects(data.orgId) })
    }
    lastStatus.current = data.status
  }, [data, queryClient])

  if (data === undefined) return null
  if (data.status !== 'active') return <StatusPanel project={data} />
  return <Outlet />
}

function StatusPanel({ project }: { project: Project }) {
  const actions = useStatusActions(project)
  return <ProjectStatusPanel project={project} actions={actions} />
}
