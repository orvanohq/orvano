import { useInfiniteQuery, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useMemo, useState } from 'react'

import { Button } from '@/components/ui/button'
import { DataTable } from '@/components/ui/data-table'
import { projectClient } from '@/lib/console-client'
import { isNotFound } from '@/lib/errors'
import { usePageTitle } from '@/lib/page-title'
import { keys, platformsQuery, projectQuery } from '@/lib/queries'
import { roleReason, useOrgRole } from '@/lib/roles'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import { PageHeading } from '@/shell/page-heading'
import type { Platform } from '@orvano/console-client'

import { NoPlatforms, platformColumns } from './-platforms/columns'
import { changedFields, PlatformDialog } from './-platforms/platform-dialog'

export const Route = createFileRoute('/_app/projects/$projectId/platforms')({
  component: PlatformsPage,
})

/**
 * The project's platforms (spec 0007, AC-17 to AC-20): the web hosts and app IDs allowed to call
 * it, 25 per page. Owners and developers add, edit, and delete them.
 */
function PlatformsPage() {
  const { projectId } = Route.useParams()
  const project = useQuery(projectQuery(projectId)).data
  usePageTitle('Platforms', project?.name)
  const role = useOrgRole()
  const queryClient = useQueryClient()
  const platforms = useInfiniteQuery(platformsQuery(projectId))
  const rows = platforms.data?.pages.flatMap((page) => page.items) ?? []
  const changeReason = meetsRole(role, 'developer') ? undefined : roleReason('developer')
  // One Add dialog for both buttons; focus returns to whichever opened it.
  const [adding, setAdding] = useState(false)
  // The Edit dialog keeps its platform while it closes, so it animates out with its content.
  const [editing, setEditing] = useState<Platform | null>(null)
  const [editOpen, setEditOpen] = useState(false)

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.platforms(projectId) })
  const columns = useMemo(
    () =>
      platformColumns({
        changeReason,
        onEdit: (platform) => {
          setEditing(platform)
          setEditOpen(true)
        },
        onDelete: async (platform) => {
          try {
            await projectClient(projectId).consolePlatforms.delete(platform.id)
          } catch (error) {
            // Someone else deleted it first: say so and show the real list (AC-10).
            if (!isNotFound(error, 'not_found')) throw error
            notifyError("Couldn't delete the platform", error)
            await queryClient.invalidateQueries({ queryKey: keys.platforms(projectId) })
            return
          }
          await queryClient.invalidateQueries({ queryKey: keys.platforms(projectId) })
          notifySuccess('Platform deleted', platform.name)
        },
      }),
    [changeReason, projectId, queryClient],
  )
  const addButton = (
    <Button
      disabledReason={changeReason}
      onClick={() => {
        setAdding(true)
      }}
    >
      Add platform
    </Button>
  )

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <PageHeading>Platforms</PageHeading>
        <div data-slot="page-actions" className="ml-auto">
          {addButton}
        </div>
      </div>
      <PlatformDialog
        open={adding}
        onOpenChange={setAdding}
        onSubmit={async (values) => {
          const platform = await projectClient(projectId).consolePlatforms.create(values)
          await refresh()
          notifySuccess('Platform added', platform.name)
        }}
      />
      {editing === null ? null : (
        <PlatformDialog
          // A fresh form for each platform, and again once a save changed it.
          key={`${editing.id}:${editing.updatedAt}`}
          platform={editing}
          open={editOpen}
          onOpenChange={setEditOpen}
          onSubmit={async (values) => {
            const changes = changedFields(editing, values)
            if (Object.keys(changes).length === 0) return
            const saved = await projectClient(projectId).consolePlatforms.update(
              editing.id,
              changes,
            )
            await refresh()
            notifySuccess('Platform saved', saved.name)
          }}
        />
      )}
      <DataTable
        label="Platforms"
        columns={columns}
        data={rows}
        loading={platforms.isPending}
        error={platforms.isError ? platforms.error : undefined}
        onRetry={() => {
          void platforms.refetch()
        }}
        hasMore={platforms.hasNextPage}
        loadingMore={platforms.isFetchingNextPage}
        onLoadMore={() => {
          void platforms.fetchNextPage()
        }}
        empty={<NoPlatforms action={addButton} />}
      />
    </div>
  )
}
