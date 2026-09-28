import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import type { RefObject } from 'react'

import { consoleApi } from '@/lib/console-client'
import { keys } from '@/lib/queries'
import { notifySuccess } from '@/lib/toast'
import { NameDialog } from '@/shell/name-dialog'

/**
 * Create org (spec 0007, AC-1), opened from the org switcher's footer and the `/orgs` header. Any
 * console account may create one and becomes its owner. On success the org list refreshes and you
 * land on the new org.
 */
export function CreateOrgDialog({
  open,
  onOpenChange,
  finalFocus,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  finalFocus?: RefObject<HTMLElement | null> | undefined
}) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  return (
    <NameDialog
      id="create-org"
      open={open}
      onOpenChange={onOpenChange}
      finalFocus={finalFocus}
      title="Create an org"
      description="An org holds projects and the teammates who work on them. You'll be its owner."
      submitLabel="Create org"
      errorTitle="Couldn't create the org"
      onSubmit={async (name) => {
        const org = await consoleApi().consoleOrgs.create({ name })
        queryClient.setQueryData(keys.org(org.id), org)
        await queryClient.invalidateQueries({ queryKey: keys.orgs })
        notifySuccess('Org created', org.name)
        await navigate({ to: '/orgs/$orgId', params: { orgId: org.id } })
      }}
    />
  )
}
