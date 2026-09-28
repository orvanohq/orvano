import { useQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'

import { CopyableId } from '@/components/ui/code-block'
import { usePageTitle } from '@/lib/page-title'
import { anyApiKeyQuery, anyPlatformQuery, orgQuery, projectQuery } from '@/lib/queries'
import { PageHeading } from '@/shell/page-heading'
import { RelativeTime } from '@/shell/relative-time'

import { ConnectYourApp } from './-overview/connect-your-app'

export const Route = createFileRoute('/_app/projects/$projectId/')({
  component: ProjectOverview,
})

// The overview: project details, then the Connect your app steps (spec 0007, AC-21). Each product
// row adds its own panels here and its entry in nav.ts.
function ProjectOverview() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  const org = useQuery({ ...orgQuery(project?.orgId ?? ''), enabled: project !== undefined }).data
  usePageTitle('Overview', project?.name)
  const hasPlatform = useQuery(anyPlatformQuery(projectId)).data
  const hasKey = useQuery(anyApiKeyQuery(projectId)).data
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
      <ConnectYourApp projectId={projectId} hasPlatform={hasPlatform} hasKey={hasKey} />
    </div>
  )
}
