import { OrvanoError } from '@orvano/console-client'
import type { QueryClient } from '@tanstack/react-query'
import type { AnyRouter } from '@tanstack/react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { isSessionError } from './session.ts'

// The module keeps its "redirect in flight" flag in module state, so each redirect test loads a
// fresh copy. `isSessionError` has no state and is imported once, so the `OrvanoError` these tests
// build is the same class it checks with `instanceof`.
async function load() {
  vi.resetModules()
  return await import('./session.ts')
}

/** The router and cache reduced to the two calls the session code makes on each. */
function bound() {
  let finishNavigation: () => void = () => undefined
  const navigate = vi.fn(
    () =>
      new Promise<void>((resolve) => {
        finishNavigation = resolve
      }),
  )
  const clear = vi.fn()
  const router = {
    latestLocation: { pathname: '/projects/scenarios0000000000a', searchStr: '?tab=keys' },
    navigate,
  }
  return {
    router,
    navigate,
    clear,
    finishNavigation: async () => {
      finishNavigation()
      // The flag resets in a promise callback; let it run.
      await new Promise((resolve) => setTimeout(resolve, 0))
    },
    bind: (session: Awaited<ReturnType<typeof load>>) => {
      session.bindSession(router as unknown as AnyRouter, { clear } as unknown as QueryClient)
    },
  }
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('isSessionError (AC-20)', () => {
  it('is true for the API answers that mean there is no usable console session (spec 0004)', () => {
    for (const code of ['console_session_required', 'token_expired', 'invalid_refresh_token'])
      expect(isSessionError(new OrvanoError(401, code, 'Sign in.', null))).toBe(true)
  })

  it('is false for another 401 code', () => {
    expect(isSessionError(new OrvanoError(401, 'invalid_token', 'Bad token.', null))).toBe(false)
  })

  it('is false for the session code on another status', () => {
    expect(isSessionError(new OrvanoError(403, 'console_session_required', 'No.', null))).toBe(
      false,
    )
  })

  it('is false for anything that is not an API error, even one shaped like it', () => {
    expect(isSessionError(new Error('console_session_required'))).toBe(false)
    expect(isSessionError({ status: 401, code: 'console_session_required' })).toBe(false)
  })
})

describe('redirectToSignIn (AC-20)', () => {
  it('sends the user to sign in with the path and query they were on', async () => {
    const session = await load()
    const app = bound()
    app.bind(session)

    session.redirectToSignIn()

    expect(app.navigate).toHaveBeenCalledWith({
      to: '/sign-in',
      search: { redirect: '/projects/scenarios0000000000a?tab=keys' },
      replace: true,
    })
  })

  it('clears the cached data', async () => {
    const session = await load()
    const app = bound()
    app.bind(session)

    session.redirectToSignIn()

    expect(app.clear).toHaveBeenCalledTimes(1)
  })

  it('redirects exactly once when several calls fail together, and the first location wins', async () => {
    const session = await load()
    const app = bound()
    app.bind(session)

    session.redirectToSignIn()
    app.router.latestLocation = { pathname: '/orgs', searchStr: '' }
    session.redirectToSignIn()
    session.redirectToSignIn()

    expect(app.navigate).toHaveBeenCalledTimes(1)
    expect(app.navigate).toHaveBeenCalledWith(
      expect.objectContaining({
        search: { redirect: '/projects/scenarios0000000000a?tab=keys' },
      }),
    )
    expect(app.clear).toHaveBeenCalledTimes(1)
  })

  it('redirects again once the earlier navigation has ended', async () => {
    const session = await load()
    const app = bound()
    app.bind(session)

    session.redirectToSignIn()
    await app.finishNavigation()
    app.router.latestLocation = { pathname: '/orgs', searchStr: '' }
    session.redirectToSignIn()

    expect(app.navigate).toHaveBeenCalledTimes(2)
    expect(app.navigate).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: { redirect: '/orgs' } }),
    )
  })

  it('does nothing before the router and cache are bound', async () => {
    const { redirectToSignIn } = await load()
    expect(() => {
      redirectToSignIn()
    }).not.toThrow()
  })
})
