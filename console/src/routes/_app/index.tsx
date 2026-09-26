import { createFileRoute, redirect } from '@tanstack/react-router'

import { clearLastProject, getLastProject } from '@/lib/last-project'
import { isNotFound } from '@/lib/errors'
import { projectQuery } from '@/lib/queries'

// `/` opens the last project when it still loads for you, otherwise the org list (AC-12).
export const Route = createFileRoute('/_app/')({
  beforeLoad: async ({ context }) => {
    const last = getLastProject()
    if (last !== undefined) {
      try {
        await context.queryClient.query({ ...projectQuery(last), staleTime: 'static' })
      } catch (error) {
        if (isNotFound(error, 'project_not_found')) clearLastProject(last)
        throw redirect({ to: '/orgs', replace: true })
      }
      throw redirect({ to: '/projects/$projectId', params: { projectId: last }, replace: true })
    }
    throw redirect({ to: '/orgs', replace: true })
  },
})
