import { afterAll, afterEach, beforeAll, expect, it } from 'vitest'
import { render } from 'vitest-browser-react'

// Spec 0004 AC-27 and spec 0005 AC-20: leaving for /sign-in never points `redirect` back at
// /sign-in. Clearing the cache while the shell was still on screen made its account query fetch
// again; that 401 arrived while you were already heading to /sign-in, and the session redirect then
// sent you to /sign-in?redirect=/sign-in, so signing in again left you on the sign in form.

const realFetch = globalThis.fetch
const testPage = window.location.pathname + window.location.search

/** The fake API's state; each test sets it. After the session ends, console calls answer 401. */
const api = { signedIn: true, unauthorizedDelayMs: 0 }

// The console client keeps the fetch it finds when its module first loads, so one fake serves every
// test, and it goes in before the app is imported.
beforeAll(() => {
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = new Request(input, init)
    const { pathname } = new URL(request.url)
    if (pathname === '/v1/console/account/session' && request.method === 'DELETE') {
      api.signedIn = false
      return new Response(null, { status: 204 })
    }
    // The sign in page asks whether setup is done; that answer needs no session.
    if (pathname === '/v1/console/install/setup') return Response.json({ setupRequired: false })
    if (pathname.startsWith('/v1/console/')) {
      if (!api.signedIn) {
        await new Promise((resolve) => setTimeout(resolve, api.unauthorizedDelayMs))
        return Response.json(
          {
            type: 'about:blank',
            title: 'Unauthorized',
            status: 401,
            code: 'console_session_required',
          },
          { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
        )
      }
      if (pathname === '/v1/console/account') return Response.json(account)
      if (pathname === '/v1/console/orgs') return Response.json({ items: [], nextCursor: null })
    }
    return realFetch(input, init)
  }
})

afterEach(() => {
  window.history.replaceState(null, '', testPage)
})

afterAll(() => {
  globalThis.fetch = realFetch
})

const account = {
  id: '01a0e581-281d-72e8-b56a-55ef6b0e8c13',
  email: 'ada@example.com',
  emailVerified: false,
  emailVerifiedAt: null,
  name: 'Ada',
  status: 'active',
  metadata: {},
  createdAt: '2026-09-27T00:00:00Z',
  lastSignInAt: '2026-09-27T00:00:00Z',
  providers: [],
  hasPassword: true,
}

/**
 * Starts the whole console at `at`, wired as main.tsx wires it, against the fake API. Once the
 * session ends (sign out, or `endSession()`), console calls answer 401 after `unauthorizedDelayMs`.
 */
async function startApp(at: string, unauthorizedDelayMs: number) {
  api.signedIn = true
  api.unauthorizedDelayMs = unauthorizedDelayMs
  const { QueryCache, QueryClient, QueryClientProvider } = await import('@tanstack/react-query')
  const { RouterProvider, createRouter } = await import('@tanstack/react-router')
  const { routeTree } = await import('../routeTree.gen')
  const { bindSession, isSessionError, redirectToSignIn } = await import('@/lib/session')
  const { PreferencesProvider } = await import('@/lib/preferences')
  const { Route: signInRoute } = await import('@/routes/sign-in')
  // In a real build /sign-in is its own chunk, fetched on the way there, and the shell stays on
  // screen meanwhile. Tests load every route up front, so this stands in for that wait.
  signInRoute.options.loader = () => new Promise<void>((resolve) => setTimeout(resolve, 200))

  window.history.replaceState(null, '', at)
  const queryClient = new QueryClient({
    queryCache: new QueryCache({
      onError: (error) => {
        if (isSessionError(error)) redirectToSignIn()
      },
    }),
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  })
  const router = createRouter({ routeTree, context: { queryClient } })
  bindSession(router, queryClient)
  const screen = await render(
    <PreferencesProvider>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </PreferencesProvider>,
  )
  await expect.element(screen.getByRole('button', { name: 'Account menu' })).toBeVisible()
  const endSession = () => {
    api.signedIn = false
  }
  return { screen, queryClient, endSession }
}

/** Waits until the sign in form shows and any late 401 has landed, then says where you are. */
async function settledLocation(screen: Awaited<ReturnType<typeof startApp>>['screen']) {
  await expect.element(screen.getByRole('button', { name: 'Sign in' })).toBeVisible()
  await new Promise((resolve) => setTimeout(resolve, 600))
  return window.location.pathname + window.location.search
}

it('signs out to plain /sign-in, with no redirect back to sign in', async () => {
  const { screen } = await startApp('/orgs', 50)

  await screen.getByRole('button', { name: 'Account menu' }).click()
  await screen.getByRole('menuitem', { name: 'Sign out' }).click()

  expect(await settledLocation(screen)).toBe('/sign-in')
})

it('sends an ended session to sign in once, pointing back at the page you were on', async () => {
  const { screen, queryClient, endSession } = await startApp('/orgs', 400)

  endSession()
  void queryClient.invalidateQueries()

  expect(await settledLocation(screen)).toBe('/sign-in?redirect=%2Forgs')
})
