import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { projectClient } from '@/lib/console-client'
import { isNotFound } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import { accountQuery, apiKeysQuery, keys, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import type { ApiKey, CreateApiKeyRequest } from '@orvano/console-client'

import { keyColumns, NoKeys } from './-keys/columns'
import { CreateKeyDialog } from './-keys/create-key-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/keys')({
  component: ApiKeysPage,
})

/**
 * The project's API keys (spec 0007, AC-12 to AC-16): oldest first, 25 per page. Owners and
 * developers create keys; a new key's secret is shown once, in the Create key dialog. Owners delete
 * any key, developers the keys they created.
 */
function ApiKeysPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('API keys', project?.name)
  const role = useOrgRole()
  const accountId = useQuery(accountQuery()).data?.id
  const queryClient = useQueryClient()
  const apiKeys = useInfiniteQuery(apiKeysQuery(projectId))
  const rows = apiKeys.data?.pages.flatMap((page) => page.items) ?? []
  const columns = useMemo(
    () =>
      keyColumns({
        role,
        accountId,
        onDelete: async (key: ApiKey) => {
          const refresh = () => queryClient.invalidateQueries({ queryKey: keys.apiKeys(projectId) })
          try {
            await projectClient(projectId).consoleApiKeys.delete(key.id)
          } catch (error) {
            // Someone else deleted it first: say so and show the real list (AC-10). A 403 stays
            // in the dialog's alert.
            if (!isNotFound(error, 'not_found')) throw error
            notifyError("Couldn't delete the key", error)
            await refresh()
            return
          }
          await refresh()
          notifySuccess('API key deleted', key.name)
        },
      }),
    [role, accountId, projectId, queryClient],
  )
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
      <CreateKeyDialog
        projectId={projectId}
        open={creating}
        onOpenChange={setCreating}
        createKey={(body: CreateApiKeyRequest) =>
          projectClient(projectId).consoleApiKeys.create(body)
        }
      />
      <DataTable
        label="API keys"
        columns={columns}
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
