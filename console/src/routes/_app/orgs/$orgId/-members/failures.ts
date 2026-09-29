import { useQueryClient } from '@tanstack/react-query'
import { useRouter } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'
import { useCallback } from 'react'

import { isNotFound } from '@/lib/errors'
import { keys, orgQuery } from '@/lib/queries'
import { refreshMoved, useStateMoved } from '@/lib/state-moved'
import { notifyError } from '@/lib/toast'

/**
 * Returns a handler for a failed member or invitation action (spec 0008, Screens). A 404 means
 * someone else already removed that member or invitation: it shows an error toast with the server's
 * message and refetches the lists and the org, and only when the org itself then answers 404 does
 * the state moved handling take over. A 409 `org_not_active` goes to `useStateMoved`. Returns true
 * when it handled the error; otherwise the caller shows it (a dialog's alert, for `last_owner`).
 */
export function useMembershipFailure(orgId: string): (title: string, error: unknown) => boolean {
  const queryClient = useQueryClient()
  const router = useRouter()
  const stateMoved = useStateMoved()
  return useCallback(
    (title, error) => {
      if (!isNotFound(error, 'not_found')) return stateMoved(title, error, { orgId })
      notifyError(title, error)
      void (async () => {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: keys.members(orgId) }),
          queryClient.invalidateQueries({ queryKey: keys.invitations(orgId) }),
        ])
        try {
          await queryClient.query({ ...orgQuery(orgId), staleTime: 0 })
        } catch (orgError) {
          if (orgError instanceof OrvanoError && isNotFound(orgError, 'not_found')) {
            await refreshMoved(queryClient, router, orgError, { orgId })
          }
        }
      })()
      return true
    },
    [orgId, queryClient, router, stateMoved],
  )
}
