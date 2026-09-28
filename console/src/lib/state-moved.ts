import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useRouter, type AnyRouter } from '@tanstack/react-router'
import { useCallback } from 'react'

import { invalidateOrgLists, keys } from '@/lib/queries'
import { notifyError } from '@/lib/toast'
import { OrvanoError } from '@orvano/console-client'

/**
 * The answers that mean the org or project changed under you, for example a teammate deleted it
 * (spec 0007, AC-10).
 */
const movedCodes: Record<number, readonly string[]> = {
  409: ['project_not_ready', 'org_not_active'],
  404: ['project_not_found', 'not_found'],
}

/** True when `error` says the org or project is no longer in the state the page showed (AC-10). */
export function isStateMoved(error: unknown): error is OrvanoError {
  return error instanceof OrvanoError && (movedCodes[error.status] ?? []).includes(error.code)
}

/** What an action touched, so a refetch reaches the right queries. */
export interface MovedTarget {
  orgId?: string | undefined
  projectId?: string | undefined
}

/**
 * Refetches what `target` names so the page shows the real state. After a 404 the entity's cached
 * copy is dropped and the route loaders run again, so its page shows the in shell not found view.
 */
export async function refreshMoved(
  queryClient: QueryClient,
  router: AnyRouter,
  error: OrvanoError,
  target: MovedTarget,
): Promise<void> {
  const gone = error.status === 404
  const work: Promise<unknown>[] = []
  if (target.projectId !== undefined) {
    if (gone) queryClient.removeQueries({ queryKey: keys.project(target.projectId) })
    else work.push(queryClient.invalidateQueries({ queryKey: keys.project(target.projectId) }))
  }
  if (target.orgId !== undefined) {
    if (gone && target.projectId === undefined) {
      queryClient.removeQueries({ queryKey: keys.org(target.orgId), exact: true })
    } else {
      work.push(queryClient.invalidateQueries({ queryKey: keys.org(target.orgId), exact: true }))
    }
    work.push(queryClient.invalidateQueries({ queryKey: keys.orgProjects(target.orgId) }))
  }
  work.push(invalidateOrgLists(queryClient))
  await Promise.all(work)
  if (gone) await router.invalidate()
}

/**
 * Returns a handler for a failed org or project action. When the state moved under you (AC-10) it
 * shows an error toast with the server's message, refetches `target`, and returns true; otherwise it
 * does nothing and returns false, so the caller reports the error its own way.
 */
export function useStateMoved(): (title: string, error: unknown, target: MovedTarget) => boolean {
  const queryClient = useQueryClient()
  const router = useRouter()
  return useCallback(
    (title, error, target) => {
      if (!isStateMoved(error)) return false
      notifyError(title, error)
      void refreshMoved(queryClient, router, error, target)
      return true
    },
    [queryClient, router],
  )
}
