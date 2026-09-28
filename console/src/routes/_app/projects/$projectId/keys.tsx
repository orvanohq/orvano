import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { usePageTitle } from '@/lib/page-title'
import { apiKeysQuery, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'

import { keyColumns, NoKeys } from './-keys/columns'
import { CreateKeyDialog } from './-keys/create-key-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/keys')({
  component: ApiKeysPage,
})

/**
 * The project's API keys (spec 0007, AC-12 to AC-15): oldest first, 25 per page. Owners and
 * developers create keys; a new key's secret is shown once, in the Create key dialog.
 */
function ApiKeysPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('API keys', project?.name)
  const role = useOrgRole()
  const apiKeys = useInfiniteQuery(apiKeysQuery(projectId))
  const rows = apiKeys.data?.pages.flatMap((page) => page.items) ?? []
  // One dialog for both buttons; focus returns to whichever opened it.
  const [creating, setCreating] = useState(false)
  const createButton = (
    <Button
      disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
      onClick={() => {
        setCreating(true)
      }}
    >
      Create key
    </Button>
  )

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>API keys</PageHeading>
        <div data-slot="page-actions" className="ml-auto">
          {createButton}
        </div>
      </div>
      <CreateKeyDialog projectId={projectId} open={creating} onOpenChange={setCreating} />
      <DataTable
        label="API keys"
        columns={keyColumns}
        data={rows}
        loading={apiKeys.isPending}
        error={apiKeys.isError ? apiKeys.error : undefined}
        onRetry={() => {
          void apiKeys.refetch()
        }}
        hasMore={apiKeys.hasNextPage}
        loadingMore={apiKeys.isFetchingNextPage}
        onLoadMore={() => {
          void apiKeys.fetchNextPage()
        }}
        empty={<NoKeys action={createButton} />}
      />
    </div>
  )
}
