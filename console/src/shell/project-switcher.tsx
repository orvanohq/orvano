import { useInfiniteQuery } from '@tanstack/react-query'
import { useNavigate, useRouterState } from '@tanstack/react-router'
import { useMemo } from 'react'

import { orgProjectsQuery } from '@/lib/queries'
import { sortActiveFirst } from '@/shell/sort'
import { Switcher, type SwitcherItem } from '@/shell/switcher'

/** The first path segment after the project ID, for example `/users`, or an empty string. */
function productSegment(pathname: string): string {
  const match = /^\/projects\/[^/]+(\/[^/]+)/.exec(pathname)
  return match?.[1] ?? ''
}

/**
 * The project switcher for the current org (AC-14). Picking a project opens the same product in
 * it when it is active, and its overview otherwise.
 */
export function ProjectSwitcher({
  orgId,
  current,
}: {
  orgId: string
  current: SwitcherItem | undefined
}) {
  const navigate = useNavigate()
  const pathname = useRouterState({ select: (state) => state.location.pathname })
  const query = useInfiniteQuery(orgProjectsQuery(orgId))
  const items = useMemo(
    () => sortActiveFirst(query.data?.pages.flatMap((page) => page.items) ?? []),
    [query.data],
  )
  return (
    <Switcher
      label="Switch project"
      placeholder="Select project"
      current={current}
      items={items}
      hasNextPage={query.hasNextPage}
      isFetchingNextPage={query.isFetchingNextPage}
      fetchNextPage={() => {
        void query.fetchNextPage()
      }}
      onPick={(project) => {
        const keep = project.status === 'active' ? productSegment(pathname) : ''
        void navigate({ href: `/projects/${project.id}${keep}` })
      }}
    />
  )
}
