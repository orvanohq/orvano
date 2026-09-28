import { useInfiniteQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { usePageTitle } from '@/lib/page-title'
import { orgProjectsQuery, orgsQuery } from '@/lib/queries'
import { ErrorPanel } from '@/shell/error-panel'
import { PageHeading } from '@/shell/page-heading'
import { CreateOrgDialog } from '@/shell/create-org-dialog'
import { statusLabel } from '@/shell/status'
import type { Org } from '@orvano/console-client'

export const Route = createFileRoute('/_app/orgs/')({
  component: OrgsPage,
})

function OrgsPage() {
  usePageTitle('Orgs')
  const orgs = useInfiniteQuery(orgsQuery(25))
  const items = orgs.data?.pages.flatMap((page) => page.items) ?? []
  const [creating, setCreating] = useState(false)

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex items-center gap-3">
        <PageHeading>Orgs</PageHeading>
        <div data-slot="page-actions" className="ml-auto">
          <Button
            onClick={() => {
              setCreating(true)
            }}
          >
            Create org
          </Button>
        </div>
      </div>
      <CreateOrgDialog open={creating} onOpenChange={setCreating} />
      {orgs.isError ? (
        <ErrorPanel
          error={orgs.error}
          onRetry={() => {
            void orgs.refetch()
          }}
        />
      ) : orgs.isPending ? (
        <div aria-busy className="flex flex-col gap-3">
          <Skeleton aria-hidden className="h-24 w-full" />
          <Skeleton aria-hidden className="h-24 w-full" />
        </div>
      ) : items.length === 0 ? (
        <p className="text-muted-foreground">You&apos;re not in any org yet.</p>
      ) : (
        items.map((org) => <OrgSection key={org.id} org={org} />)
      )}
      {orgs.hasNextPage ? (
        <div className="flex justify-center">
          <Button
            variant="outline"
            loading={orgs.isFetchingNextPage}
            onClick={() => {
              void orgs.fetchNextPage()
            }}
          >
            Load more
          </Button>
        </div>
      ) : null}
    </div>
  )
}

/** One org with its oldest 6 projects as cards, and "View all" when there are more (AC in spec 0005). */
function OrgSection({ org }: { org: Org }) {
  const projects = useInfiniteQuery(orgProjectsQuery(org.id, 6))
  const first = projects.data?.pages[0]
  const dimmed = org.status === 'deleting'
  const headingId = `org-${org.id}`

  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <h2
          id={headingId}
          className={dimmed ? 'text-lg/7 font-semibold opacity-60' : 'text-lg/7 font-semibold'}
        >
          <Link to="/orgs/$orgId" params={{ orgId: org.id }} className="hover:underline">
            {org.name}
          </Link>
        </h2>
        {dimmed ? (
          <Badge variant="status" tone="neutral">
            Deleting
          </Badge>
        ) : null}
        {first?.nextCursor != null ? (
          <Link
            to="/orgs/$orgId"
            params={{ orgId: org.id }}
            className="ml-auto text-body text-link hover:underline"
          >
            View all<span className="sr-only"> projects in {org.name}</span>
          </Link>
        ) : null}
      </div>
      {projects.isError ? (
        <ErrorPanel
          error={projects.error}
          onRetry={() => {
            void projects.refetch()
          }}
        />
      ) : first === undefined ? (
        <Skeleton aria-hidden className="h-16 w-full" />
      ) : first.items.length === 0 ? (
        <p className="text-muted-foreground">No projects yet.</p>
      ) : (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {first.items.map((project) => {
            const { label, tone } = statusLabel(project.status)
            return (
              <li key={project.id}>
                <Link
                  to="/projects/$projectId"
                  params={{ projectId: project.id }}
                  className="flex h-full flex-col gap-2 rounded-lg border border-border bg-card p-3 hover:bg-accent"
                >
                  <span className="truncate font-medium">{project.name}</span>
                  <span className="flex items-center justify-between gap-2">
                    <code className="truncate font-mono text-small text-muted-foreground">
                      {project.id}
                    </code>
                    {project.status === 'active' ? null : (
                      <Badge variant="status" tone={tone}>
                        {label}
                      </Badge>
                    )}
                  </span>
                </Link>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
