import { afterEach, expect, it } from 'vitest'
import { render } from 'vitest-browser-react'

// Spec 0006 AC-23: the setup link's token survives the fragment being removed. Removing it makes the
// router load /setup a second time, which once replaced the token with nothing, so the first admin
// was always refused with setup_token_invalid.

const token = 'ost_setupSetupSetupSetupSetupSetupSetupSetup000'
const realFetch = globalThis.fetch
const testPage = window.location.pathname + window.location.search

afterEach(() => {
  globalThis.fetch = realFetch
  window.history.replaceState(null, '', testPage)
})

it('sends the token from the setup link after removing it from the address bar', async () => {
  const sent: unknown[] = []
  // The console client keeps the fetch it finds when its module loads, so the fake goes in first
  // and the app is imported after it.
  globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = new Request(input, init)
    const { pathname } = new URL(request.url)
    if (pathname === '/v1/console/install/setup') return Response.json({ setupRequired: true })
    if (pathname === '/v1/console/account' && request.method === 'POST') {
      sent.push(await request.json())
      return Response.json(
        { type: 'about:blank', title: 'Forbidden', status: 403, code: 'setup_token_invalid' },
        { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
      )
    }
    return realFetch(input, init)
  }
  const { QueryClient, QueryClientProvider } = await import('@tanstack/react-query')
  const { RouterProvider, createRouter } = await import('@tanstack/react-router')
  const { routeTree } = await import('../routeTree.gen')

  window.history.replaceState(null, '', `/setup#${token}`)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createRouter({ routeTree, context: { queryClient } })
  const screen = await render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )

  await screen.getByLabelText('Email').fill('ada@example.com')
  expect(window.location.hash).toBe('')
  await screen.getByLabelText('Password').fill('correct horse battery')
  await screen.getByRole('button', { name: 'Create the first admin' }).click()

  await expect.poll(() => sent).toHaveLength(1)
  expect(sent[0]).toMatchObject({ email: 'ada@example.com', setupToken: token })
})
