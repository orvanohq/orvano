import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useMemo, useState } from 'react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { projectClient } from '@/lib/console-client'
import { usePageTitle } from '@/lib/page-title'
import { keys, projectQuery, tablesQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import type { CreateTableRequest } from '@orvano/console-client'

import { NewTableDialog } from '../-databases/new-table-dialog'
import { NoTables, tableColumns } from '../-databases/parts'

export const Route = createFileRoute('/_app/projects/$projectId/databases/$database/')({
  component: DatabasePage,
})

/**
 * A database's tables (spec 0015, AC-32): name, estimated rows, and a Read only badge for tables
 * whose rows the API can't change, 25 at a time. Owners and developers create tables; a new table
 * opens at once.
 */
function DatabasePage() {
  const { projectId, database } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle(database, project?.name)
  const role = useOrgRole()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const tables = useInfiniteQuery(tablesQuery(projectId, database))
  const rows = tables.data?.pages.flatMap((page) => page.items) ?? []
  const columns = useMemo(() => tableColumns(projectId, database), [projectId, database])
  const [creating, setCreating] = useState(false)

  const newTableButton = (
    <Button
      disabledReason={meetsRole(role, 'developer') ? undefined : roleReason('developer')}
      onClick={() => {
        setCreating(true)
      }}
    >
      New table
    </Button>
  )

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex flex-col gap-1">
          <Link
            to="/projects/$projectId/databases"
            params={{ projectId }}
            className="text-small text-link hover:underline"
          >
            Databases
          </Link>
          <PageHeading>
            <span className="font-mono">{database}</span>
          </PageHeading>
        </div>
        <div data-slot="page-actions" className="ml-auto">
          {newTableButton}
        </div>
      </div>
      <NewTableDialog
        open={creating}
        onOpenChange={setCreating}
        onCreate={async (request: CreateTableRequest) => {
          const table = await projectClient(projectId).consoleTables.create(database, request)
          await queryClient.invalidateQueries({ queryKey: keys.tables(projectId, database) })
          notifySuccess('Table created', table.name)
          await navigate({
            to: '/projects/$projectId/databases/$database/tables/$table',
            params: { projectId, database, table: table.name },
          })
        }}
      />
      <DataTable
        label="Tables"
        columns={columns}
        data={rows}
        getRowId={(table) => table.name}
        loading={tables.isPending}
        error={tables.isError ? tables.error : undefined}
        onRetry={() => {
          void tables.refetch()
        }}
        hasMore={tables.hasNextPage}
        loadingMore={tables.isFetchingNextPage}
        onLoadMore={() => {
          void tables.fetchNextPage()
        }}
        empty={<NoTables action={newTableButton} />}
      />
    </div>
  )
}
