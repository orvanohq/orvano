import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { createFileRoute, Link } from '@tanstack/react-router'
import { Info } from 'lucide-react'
import { useMemo } from 'react'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { DataTable } from '@/components/ui/data-table'
import { usePageTitle } from '@/lib/page-title'
import { rowsQuery, tableQuery } from '@/lib/queries'
import { PageHeading } from '@/shell/page-heading'

import { NoRows, rowColumns, rowId } from '../../-databases/parts'

export const Route = createFileRoute('/_app/projects/$projectId/databases/$database/tables/$table')(
  {
    component: TablePage,
  },
)

/**
 * A table's rows (spec 0015, AC-33): a grid of 25 rows in created order with Load more, one column
 * per table column. Apps can't use tables until permission rules exist, which the banner says.
 */
function TablePage() {
  const { projectId, database, table } = Route.useParams()
  usePageTitle(table, database)
  const info = useQuery(tableQuery(projectId, database, table))
  const rows = useInfiniteQuery({
    ...rowsQuery(projectId, database, table),
    enabled: info.data?.readable === true,
  })
  const items = rows.data?.pages.flatMap((page) => page.items) ?? []
  const columns = useMemo(() => rowColumns(info.data?.columns ?? []), [info.data])

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link
          to="/projects/$projectId/databases/$database"
          params={{ projectId, database }}
          className="text-small font-mono text-link hover:underline"
        >
          {database}
        </Link>
        <PageHeading>
          <span className="font-mono">{table}</span>
        </PageHeading>
      </div>
      <Alert role="note">
        <Info aria-hidden />
        <AlertTitle>Apps can&apos;t read or write rows yet</AlertTitle>
        <AlertDescription>
          Client access comes with permission rules. Server SDKs with an API key and this console
          work now.
        </AlertDescription>
      </Alert>
      {info.data !== undefined && !info.data.readable ? (
        <Alert role="status">
          <Info aria-hidden />
          <AlertTitle>This table has no primary key</AlertTitle>
          <AlertDescription>
            Add a primary key in SQL to see and use its rows through Orvano.
          </AlertDescription>
        </Alert>
      ) : (
        <DataTable
          label={`Rows of ${table}`}
          columns={columns}
          data={items}
          getRowId={rowId}
          loading={info.isPending || rows.isPending}
          error={info.isError ? info.error : rows.isError ? rows.error : undefined}
          onRetry={() => {
            void (info.isError ? info.refetch() : rows.refetch())
          }}
          hasMore={rows.hasNextPage}
          loadingMore={rows.isFetchingNextPage}
          onLoadMore={() => {
            void rows.fetchNextPage()
          }}
          empty={<NoRows />}
        />
      )}
    </div>
  )
}
