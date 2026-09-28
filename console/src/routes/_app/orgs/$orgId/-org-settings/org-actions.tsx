import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { consoleApi } from '@/lib/console-client'
import { invalidateOrgLists, keys } from '@/lib/queries'
import { roleReason } from '@/lib/roles'
import { useStateMoved } from '@/lib/state-moved'
import { notifyError, notifySuccess } from '@/lib/toast'
import { meetsRole } from '@/shell/nav'
import type { Org, Project } from '@orvano/console-client'

/**
 * Writes an org the API returned into its query and marks your org lists stale, so the switcher
 * shows its new name, or moves it to the end dimmed while it is being deleted (spec 0007, AC-3, AC-4).
 */
export async function applyOrg(queryClient: QueryClient, org: Org): Promise<void> {
  queryClient.setQueryData(keys.org(org.id), org)
  await invalidateOrgLists(queryClient)
}

/**
 * "Restore org" (spec 0007, AC-5): in the Deleting banner on every org page and in the Danger zone.
 * One click, no confirm; a spinner while it runs. Owners only. Restoring the org restores none of its
 * projects.
 */
export function RestoreOrgButton({
  org,
  variant = 'primary',
}: {
  org: Org
  variant?: 'primary' | 'outline'
}) {
  const queryClient = useQueryClient()
  const stateMoved = useStateMoved()
  const [pending, setPending] = useState(false)
  return (
    <Button
      variant={variant}
      loading={pending}
      disabledReason={meetsRole(org.role, 'owner') ? undefined : roleReason('owner')}
      onClick={() => {
        setPending(true)
        consoleApi()
          .consoleOrgs.restore(org.id)
          .then(
            async (restored) => {
              await applyOrg(queryClient, restored)
              notifySuccess('Org restored', restored.name)
            },
            (error: unknown) => {
              const title = "Couldn't restore the org"
              if (!stateMoved(title, error, { orgId: org.id })) notifyError(title, error)
            },
          )
          .finally(() => {
            setPending(false)
          })
      }}
    >
      Restore org
    </Button>
  )
}

/**
 * Why Delete org is unavailable, or undefined when it is (spec 0007, AC-4). The check reads the first
 * page of projects only; the server's 409 `org_not_empty` covers the rest.
 */
export function deleteOrgReason(
  org: Org,
  firstPage: readonly Project[] | undefined,
): string | undefined {
  if (!meetsRole(org.role, 'owner')) return roleReason('owner')
  if (firstPage === undefined) return 'Checking projects…'
  if (firstPage.some((project) => project.status !== 'deleting')) return 'Delete its projects first'
  return undefined
}
