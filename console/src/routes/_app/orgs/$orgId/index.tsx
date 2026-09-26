import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Link, createFileRoute } from '@tanstack/react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { FolderKanban } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { CopyableId } from '@/components/ui/code-block'
import { DataTable } from '@/components/ui/data-table'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { usePageTitle } from '@/lib/page-title'
import { orgProjectsQuery, orgQuery } from '@/lib/queries'
import { PageHeading } from '@/shell/page-heading'
import { RelativeTime } from '@/shell/relative-time'
import { statusLabel } from '@/shell/status'
import type { Project } from '@orvano/console-client'

export const Route = createFileRoute('/_app/orgs/$orgId/')({
  component: OrgProjectsPage,
})

const columns: ColumnDef<Project>[] = [
  {
    accessorKey: 'name',
    header: 'Name',
    cell: ({ row }) => (
      <Link
        to="/projects/$projectId"
        params={{ projectId: row.original.id }}
        className="font-medium text-link hover:underline"
      >
        {row.original.name}
      </Link>
    ),
  },
  {
    accessorKey: 'status',
    header: 'Status',
    cell: ({ row }) => {
      const { label, tone } = statusLabel(row.original.status)
      return (
        <Badge variant="status" tone={tone}>
          {label}
        </Badge>
      )
    },
  },
  {
    accessorKey: 'id',
    header: 'ID',
    cell: ({ row }) => <CopyableId value={row.original.id} label={`ID of ${row.original.name}`} />,
    enableSorting: false,
  },
  {
    accessorKey: 'createdAt',
    header: 'Created',
    cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
  },
]

function OrgProjectsPage() {
  const { orgId } = Route.useParams()
  const org = useQuery(orgQuery(orgId)).data
  usePageTitle('Projects', org?.name)
  const projects = useInfiniteQuery(orgProjectsQuery(orgId, 25))
  const rows = projects.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <>
      <div className="flex items-center gap-3">
        <PageHeading>Projects</PageHeading>
        {/* Row 7 puts "Create project" here. */}
        <div data-slot="page-actions" className="ml-auto" />
      </div>
      <DataTable
        label="Projects"
        columns={columns}
        data={rows}
        sortable
        loading={projects.isPending}
        error={projects.isError ? projects.error : undefined}
        onRetry={() => {
          void projects.refetch()
        }}
        hasMore={projects.hasNextPage}
        loadingMore={projects.isFetchingNextPage}
        onLoadMore={() => {
          void projects.fetchNextPage()
        }}
        empty={
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <FolderKanban aria-hidden />
              </EmptyMedia>
              <EmptyTitle>No projects yet</EmptyTitle>
              <EmptyDescription>Projects you create in this org appear here.</EmptyDescription>
            </EmptyHeader>
            {/* Row 7 fills this action slot. */}
            <div data-slot="empty-action" />
          </Empty>
        }
      />
    </>
  )
}
