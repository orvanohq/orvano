import { useQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'

import { CopyableId } from '@/components/ui/code-block'
import { usePageTitle } from '@/lib/page-title'
import { orgQuery, projectQuery } from '@/lib/queries'
import { PageHeading } from '@/shell/page-heading'
import { RelativeTime } from '@/shell/relative-time'

export const Route = createFileRoute('/_app/projects/$projectId/')({
  component: ProjectOverview,
})

// Placeholder overview: each product row adds its own panels here and its entry in nav.ts.
function ProjectOverview() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  const org = useQuery({ ...orgQuery(project?.orgId ?? ''), enabled: project !== undefined }).data
  usePageTitle('Overview', project?.name)
  if (project === undefined) return null

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <PageHeading>{project.name}</PageHeading>
      <dl className="grid max-w-xl grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
        <dt className="text-muted-foreground">Project ID</dt>
        <dd>
          <CopyableId value={project.id} label="project ID" />
        </dd>
        <dt className="text-muted-foreground">Org</dt>
        <dd>
          {org === undefined ? null : (
            <Link
              to="/orgs/$orgId"
              params={{ orgId: org.id }}
              className="text-link hover:underline"
            >
              {org.name}
            </Link>
          )}
        </dd>
        <dt className="text-muted-foreground">Created</dt>
        <dd>
          <RelativeTime iso={project.createdAt} />
        </dd>
      </dl>
    </div>
  )
}
