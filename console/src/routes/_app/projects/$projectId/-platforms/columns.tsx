import type { ColumnDef } from '@tanstack/react-table'
import { MonitorSmartphone } from 'lucide-react'
import type { ReactNode } from 'react'

import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { platformTypes } from '@/lib/platform-types'
import { RelativeTime } from '@/shell/relative-time'
import type { Platform, PlatformType } from '@orvano/console-client'

/** A platform type as its icon and label; the label carries the meaning (spec 0007, AC-17). */
export function PlatformTypeLabel({ type }: { type: PlatformType }) {
  const { icon: Icon, label } = platformTypes[type]
  return (
    <span className="inline-flex items-center gap-2">
      <Icon aria-hidden className="size-(--icon) text-muted-foreground" />
      {label}
    </span>
  )
}

/** The Platforms table (AC-17). */
export const platformColumns: ColumnDef<Platform>[] = [
  {
    accessorKey: 'type',
    header: 'Type',
    cell: ({ row }) => <PlatformTypeLabel type={row.original.type} />,
  },
  {
    accessorKey: 'name',
    header: 'Name',
    cell: ({ row }) => <span className="font-medium">{row.original.name}</span>,
  },
  {
    accessorKey: 'identifier',
    header: 'Identifier',
    cell: ({ row }) => (
      <code className="font-mono text-mono break-all">{row.original.identifier}</code>
    ),
  },
  {
    accessorKey: 'createdAt',
    header: 'Added',
    cell: ({ row }) => <RelativeTime iso={row.original.createdAt} />,
  },
]

/** The empty Platforms table, with "Add platform" (AC-17). */
export function NoPlatforms({ action }: { action: ReactNode }) {
  return (
    <Empty className="border">
      <EmptyHeader>
        <EmptyMedia variant="icon">
          <MonitorSmartphone aria-hidden />
        </EmptyMedia>
        <EmptyTitle>No platforms yet</EmptyTitle>
        <EmptyDescription>
          Add the web hosts and app IDs allowed to call this project from a browser or device.
        </EmptyDescription>
      </EmptyHeader>
      <div data-slot="empty-action">{action}</div>
    </Empty>
  )
}
