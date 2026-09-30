import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { Mail } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { DataTable } from '@/components/ui/data-table'
import { Empty, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { formatDateTime, formatFull } from '@/lib/format'
import type { EmailLogEntry, EmailPage } from '@orvano/console-client'

import { reasonMessages, statusInfo, templateLabels } from './email-log'

/** A time in the viewer's own time zone, with the full form on hover. */
function LocalTime({ iso }: { iso: string }) {
  return (
    <time dateTime={iso} title={formatFull(iso)} className="whitespace-nowrap">
      {formatDateTime(iso)}
    </time>
  )
}

const columns: ColumnDef<EmailLogEntry>[] = [
  {
    accessorKey: 'template',
    header: 'Template',
    cell: ({ row }) => (
      <span className="font-medium whitespace-nowrap">{templateLabels[row.original.template]}</span>
    ),
  },
  {
    accessorKey: 'recipient',
    header: 'To',
    cell: ({ row }) => <span className="font-mono">{row.original.recipient}</span>,
  },
  {
    accessorKey: 'status',
    header: 'Status',
    cell: ({ row }) => {
      const info = statusInfo[row.original.status]
      return (
        <Badge variant="status" tone={info.tone}>
          {info.label}
        </Badge>
      )
    },
  },
  { accessorKey: 'attempts', header: 'Attempts' },
  {
    id: 'reason',
    header: 'Reason',
    cell: ({ row }) =>
      row.original.errorCode === null ? null : (
        <span className="text-muted-foreground">{reasonMessages[row.original.errorCode]}</span>
      ),
  },
  {
    accessorKey: 'createdAt',
    header: 'Created',
    cell: ({ row }) => <LocalTime iso={row.original.createdAt} />,
  },
  {
    accessorKey: 'completedAt',
    header: 'Completed',
    cell: ({ row }) =>
      row.original.completedAt === null ? null : <LocalTime iso={row.original.completedAt} />,
  },
]

/**
 * The email log (spec 0009, AC-20 and AC-21): Template, To (the masked recipient), Status, Attempts,
 * Reason, Created, and Completed, newest first, with "Load more". The project's Log tab and
 * `/install` both show it, each over its own query.
 */
export function EmailLogTable({
  label,
  emails,
}: {
  /** Names the table's scroll region. */
  label: string
  emails: UseInfiniteQueryResult<InfiniteData<EmailPage>>
}) {
  return (
    <DataTable
      label={label}
      columns={columns}
      data={emails.data?.pages.flatMap((page) => page.items) ?? []}
      loading={emails.isPending}
      error={emails.isError ? emails.error : undefined}
      onRetry={() => {
        void emails.refetch()
      }}
      hasMore={emails.hasNextPage}
      loadingMore={emails.isFetchingNextPage}
      onLoadMore={() => {
        void emails.fetchNextPage()
      }}
      empty={
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <Mail aria-hidden />
            </EmptyMedia>
            <EmptyTitle>No emails in the last 30 days</EmptyTitle>
          </EmptyHeader>
        </Empty>
      }
    />
  )
}
