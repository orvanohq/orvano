import { useInfiniteQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useMemo } from 'react'

import { orgsQuery } from '@/lib/queries'
import { Switcher, type SwitcherItem } from '@/shell/switcher'
import { sortActiveFirst } from '@/shell/sort'

/** The org switcher: your orgs from `consoleOrgs.list`, 100 per page (AC-13). */
export function OrgSwitcher({ current }: { current: SwitcherItem | undefined }) {
  const navigate = useNavigate()
  const query = useInfiniteQuery(orgsQuery())
  // The whole loaded list is sorted again after every page, never one page at a time.
  const items = useMemo(
    () => sortActiveFirst(query.data?.pages.flatMap((page) => page.items) ?? []),
    [query.data],
  )
  return (
    <Switcher
      label="Switch org"
      placeholder="Select org"
      current={current}
      items={items}
      hasNextPage={query.hasNextPage}
      isFetchingNextPage={query.isFetchingNextPage}
      fetchNextPage={() => {
        void query.fetchNextPage()
      }}
      onPick={(org) => {
        void navigate({ to: '/orgs/$orgId', params: { orgId: org.id } })
      }}
    />
  )
}
