import { useInfiniteQuery } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { useMemo, useRef, useState } from 'react'

import { orgsQuery } from '@/lib/queries'
import { CreateOrgDialog } from '@/shell/create-org-dialog'
import { Switcher, type SwitcherItem } from '@/shell/switcher'
import { sortActiveFirst } from '@/shell/sort'

/**
 * The org switcher: your orgs from `consoleOrgs.list`, 100 per page (AC-13), with "Create org" in its
 * footer (spec 0007, AC-1).
 */
export function OrgSwitcher({ current }: { current: SwitcherItem | undefined }) {
  const navigate = useNavigate()
  const query = useInfiniteQuery(orgsQuery())
  const [creating, setCreating] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  // The whole loaded list is sorted again after every page, never one page at a time.
  const items = useMemo(
    () => sortActiveFirst(query.data?.pages.flatMap((page) => page.items) ?? []),
    [query.data],
  )
  return (
    <>
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
        triggerRef={triggerRef}
        footerAction={{
          label: 'Create org',
          onSelect: () => {
            setCreating(true)
          },
        }}
      />
      <CreateOrgDialog open={creating} onOpenChange={setCreating} finalFocus={triggerRef} />
    </>
  )
}
