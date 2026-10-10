import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useMemo } from 'react'

import { DataTable } from '@/components/ui/data-table'
import { usePageTitle } from '@/lib/page-title'
import { databasesQuery, projectQuery } from '@/lib/queries'
import { PageHeading } from '@/shell/page-heading'

import { databaseColumns } from './-databases/parts'

export const Route = createFileRoute('/_app/projects/$projectId/databases/')({
  component: DatabasesPage,
})

/**
 * The project's databases (spec 0015, AC-31): `main` first, then by slug, each opening its tables.
 * Every project has `main`, its own Postgres schema.
 */
function DatabasesPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Databases', project?.name)
  const databases = useInfiniteQuery(databasesQuery(projectId))
  const rows = databases.data?.pages.flatMap((page) => page.items) ?? []
  const columns = useMemo(() => databaseColumns(projectId), [projectId])

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <PageHeading>Databases</PageHeading>
        <p className="text-muted-foreground">
          Each database is a Postgres schema of real tables. Every project has <code>main</code>.
        </p>
      </div>
      <DataTable
        label="Databases"
        columns={columns}
        data={rows}
        getRowId={(database) => database.id}
        loading={databases.isPending}
        error={databases.isError ? databases.error : undefined}
        onRetry={() => {
          void databases.refetch()
        }}
        hasMore={databases.hasNextPage}
        loadingMore={databases.isFetchingNextPage}
        onLoadMore={() => {
          void databases.fetchNextPage()
        }}
      />
    </div>
  )
}
