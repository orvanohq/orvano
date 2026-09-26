import type { QueryClient } from '@tanstack/react-query'
import type { AnyRouter } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'

let router: AnyRouter | undefined
let queryClient: QueryClient | undefined
let inFlight = false

/** Gives the session code the router and cache it redirects and clears; called once at startup. */
export function bindSession(boundRouter: AnyRouter, boundQueryClient: QueryClient): void {
  router = boundRouter
  queryClient = boundQueryClient
}

/** True for the API's "no console session" answer. Row 8 adds its own session codes here. */
export function isSessionError(error: unknown): boolean {
  return (
    error instanceof OrvanoError &&
    error.status === 401 &&
    error.code === 'console_session_required'
  )
}

/**
 * Sends the user to `/sign-in?redirect=<where they were>` and clears cached data, exactly once:
 * while a redirect is in flight, later calls return at once, so when several calls fail together the
 * first one's location wins (spec 0005 AC-20). The location is the latest one the router was asked
 * for, so a failure while a navigation is still loading redirects back to where it was heading.
 */
export function redirectToSignIn(): void {
  if (inFlight || router === undefined || queryClient === undefined) return
  inFlight = true
  const { pathname, searchStr } = router.latestLocation
  const redirect = `${pathname}${searchStr}`
  queryClient.clear()
  void router.navigate({ to: '/sign-in', search: { redirect }, replace: true }).finally(() => {
    inFlight = false
  })
}
