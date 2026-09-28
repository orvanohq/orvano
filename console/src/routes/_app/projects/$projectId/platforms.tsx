import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { usePageTitle } from '@/lib/page-title'
import { platformsQuery, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'

import { NoPlatforms, platformColumns } from './-platforms/columns'
import { PlatformDialog } from './-platforms/platform-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/platforms')({
  component: PlatformsPage,
})

/**
 * The project's platforms (spec 0007, AC-17 and AC-18): the web hosts and app IDs allowed to call
 * it, 25 per page. Owners and developers add them.
 */
function PlatformsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Platforms', project?.name)
  const role = useOrgRole()
  const platforms = useInfiniteQuery(platformsQuery(projectId))
  const rows = platforms.data?.pages.flatMap((page) => page.items) ?? []
  // One dialog for both buttons; focus returns to whichever opened it.
  const [adding, setAdding] = useState(false)
  const addButton = (
    <Button
      disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
      onClick={() => {
        setAdding(true)
      }}
    >
      Add platform
    </Button>
  )

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>Platforms</PageHeading>
        <div data-slot="page-actions" className="ml-auto">
          {addButton}
        </div>
      </div>
      <PlatformDialog projectId={projectId} open={adding} onOpenChange={setAdding} />
      <DataTable
        label="Platforms"
        columns={platformColumns}
        data={rows}
        loading={platforms.isPending}
        error={platforms.isError ? platforms.error : undefined}
        onRetry={() => {
          void platforms.refetch()
        }}
        hasMore={platforms.hasNextPage}
        loadingMore={platforms.isFetchingNextPage}
        onLoadMore={() => {
          void platforms.fetchNextPage()
        }}
        empty={<NoPlatforms action={addButton} />}
      />
    </div>
  )
}
