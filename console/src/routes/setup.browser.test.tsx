import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render } from 'vitest-browser-react'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, type FakeApi } from '@/test/fake-api'

// Spec 0006 AC-23: the installer's setup link, /setup#<token>, creates the first admin.

const api: FakeApi = installFakeApi()
const token = 'ost_setupSetupSetupSetupSetupSetupSetupSetup000'
const testPage = window.location.pathname + window.location.search
const invalidLink =
  'This setup link is not valid. Run the installer again on your server to see the right link.'

const signUps = () =>
  api.requests.filter(
    (request) => request.method === 'POST' && request.path === '/v1/console/account',
  )

beforeEach(() => {
  api.setupRequired = true
  api.orgs = []
  api.requests = []
})

afterEach(() => {
  window.history.replaceState(null, '', testPage)
})

async function fillForm(screen: Awaited<ReturnType<typeof renderApp>>['screen']) {
  await screen.getByLabelText('Name').fill('Ada Lovelace')
  await screen.getByLabelText('Email').fill('ada@example.com')
  await screen.getByLabelText('Password').fill('correct horse battery')
}

// Removing the fragment makes the router load /setup a second time, which once replaced the token
// with nothing, so the first admin was always refused with setup_token_invalid. This test runs the
// router on the real address bar, since the route reads the fragment from window.location.
it('sends the token from the setup link after removing it from the address bar', async () => {
  api.failNext('POST', /^\/v1\/console\/account$/, 403, 'setup_token_invalid', 'raw')
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

  await expect.poll(() => signUps()).toHaveLength(1)
  expect(signUps()[0]?.body).toMatchObject({ email: 'ada@example.com', setupToken: token })
})

describe('/setup', () => {
  it('shows the create the first admin form while setup is required', async () => {
    const { screen } = await renderApp('/setup')

    await expect
      .element(screen.getByRole('heading', { name: 'Create the first admin' }))
      .toBeVisible()
    await expect.element(screen.getByLabelText('Name')).toBeVisible()
    await expect.element(screen.getByLabelText('Email')).toBeVisible()
    await expect.element(screen.getByLabelText('Password')).toBeVisible()
    await expect
      .element(screen.getByRole('button', { name: 'Create the first admin' }))
      .toBeEnabled()
  })

  it('lands you signed in on your new personal org', async () => {
    const { screen, router } = await renderApp('/setup')
    await fillForm(screen)

    await screen.getByRole('button', { name: 'Create the first admin' }).click()

    const personal = () => api.orgs.at(0)?.id
    await expect.poll(() => personal()).toBeDefined()
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${personal() ?? ''}`)
    expect(signUps()[0]?.body).toMatchObject({
      name: 'Ada Lovelace',
      email: 'ada@example.com',
      password: 'correct horse battery',
    })
  })

  it('sends a blank name as null', async () => {
    const { screen } = await renderApp('/setup')
    await screen.getByLabelText('Email').fill('ada@example.com')
    await screen.getByLabelText('Password').fill('correct horse battery')

    await screen.getByRole('button', { name: 'Create the first admin' }).click()

    await expect.poll(() => signUps()).toHaveLength(1)
    expect(signUps()[0]?.body).toMatchObject({ name: null })
  })

  it('says the link is not valid when the server refuses the token, and stays on the form', async () => {
    api.failNext('POST', /^\/v1\/console\/account$/, 403, 'setup_token_invalid', 'raw')
    const { screen, router } = await renderApp('/setup')
    await fillForm(screen)

    await screen.getByRole('button', { name: 'Create the first admin' }).click()

    await expect.element(screen.getByText(invalidLink)).toBeVisible()
    expect(router.state.location.pathname).toBe('/setup')
    expect(api.orgs).toHaveLength(0)
  })

  it('sends you to sign in once the install has its admin', async () => {
    api.setupRequired = false

    const { router } = await renderApp('/setup')

    await expect.poll(() => router.state.location.pathname).toBe('/sign-in')
  })

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode, with the invalid link message showing`, async () => {
      setMode(theme, 'comfortable')
      api.failNext('POST', /^\/v1\/console\/account$/, 403, 'setup_token_invalid', 'raw')
      const { screen } = await renderApp('/setup')
      await fillForm(screen)
      await screen.getByRole('button', { name: 'Create the first admin' }).click()
      await expect.element(screen.getByText(invalidLink)).toBeVisible()

      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })

      expect(results.violations.map((violation) => violation.id)).toEqual([])
    })
  }
})
