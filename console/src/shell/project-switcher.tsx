import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { useNavigate, useRouterState } from '@tanstack/react-router'
import { useMemo, useRef, useState } from 'react'

import { orgProjectsQuery, orgQuery } from '@/lib/queries'
import { CreateProjectDialog, createProjectReason } from '@/shell/create-project-dialog'
import { sortActiveFirst } from '@/shell/sort'
import { Switcher, type SwitcherItem } from '@/shell/switcher'

/** The first path segment after the project ID, for example `/users`, or an empty string. */
function productSegment(pathname: string): string {
  const match = /^\/projects\/[^/]+(\/[^/]+)/.exec(pathname)
  return match?.[1] ?? ''
}

/**
 * The project switcher for the current org (AC-14). Picking a project opens the same product in
 * it when it is active, and its overview otherwise. Its footer holds "Create project" for this org
 * (spec 0007, AC-6).
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
  const org = useQuery(orgQuery(orgId)).data
  const [creating, setCreating] = useState(false)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const items = useMemo(
    () => sortActiveFirst(query.data?.pages.flatMap((page) => page.items) ?? []),
    [query.data],
  )
  return (
    <>
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
        triggerRef={triggerRef}
        footerAction={{
          label: 'Create project',
          disabledReason: createProjectReason(org),
          onSelect: () => {
            setCreating(true)
          },
        }}
      />
      <CreateProjectDialog
        orgId={orgId}
        open={creating}
        onOpenChange={setCreating}
        finalFocus={triggerRef}
      />
    </>
  )
}
