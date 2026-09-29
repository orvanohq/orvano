import axe from 'axe-core'
import { beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, type FakeApi } from '@/test/fake-api'

// Spec 0008, AC-23 and AC-26: /sign-up for installs with open sign up.

const api: FakeApi = installFakeApi()
const inviteOnly = 'Sign up on this server is by invitation. Ask an org owner for an invite link.'

const text = () => document.body.textContent
const submit = () =>
  document.querySelector<HTMLButtonElement>('form button[type=submit]') ?? document.body
const signUps = () =>
  api.requests.filter(
    (request) => request.method === 'POST' && request.path === '/v1/console/account',
  )

beforeEach(() => {
  setMode('dark', 'compact')
  api.setupRequired = false
  api.consoleSignup = 'open'
  api.signedIn = false
  api.orgs = []
  api.requests = []
})

describe('/sign-up', () => {
  it('sends you to /setup while the install waits for its first admin', async () => {
    api.setupRequired = true
    const { router } = await renderApp('/sign-up')
    await expect.poll(() => router.state.location.pathname).toBe('/setup')
  })

  it('sends you home when you are already signed in', async () => {
    api.signedIn = true
    const { router } = await renderApp('/sign-up')
    await expect.poll(() => router.state.location.pathname).not.toBe('/sign-up')
  })

  it('says sign up is by invitation on an invite only install, with a link to sign in', async () => {
    api.consoleSignup = 'invite'
    await renderApp('/sign-up')
    await expect.poll(text).toContain(inviteOnly)
    expect(document.querySelector('a[href="/sign-in"]')).not.toBeNull()
    expect(document.querySelector('form')).toBeNull()
  })

  it('creates the account and lands in the shell, titled Sign up', async () => {
    const { screen, router } = await renderApp('/sign-up')
    await expect.element(screen.getByRole('heading', { name: 'Create your account' })).toBeVisible()
    await expect.poll(() => document.title).toBe('Sign up · Orvano')
    await screen.getByLabelText('Email').fill('linus@example.com')
    await screen.getByLabelText('Password').fill('correct horse battery')
    await userEvent.click(submit())
    await expect.poll(() => router.state.location.pathname).not.toBe('/sign-up')
    expect(signUps()[0]?.body).toMatchObject({ email: 'linus@example.com', name: null })
  })

  it('shows the invite only message when the mode closed after the page loaded', async () => {
    const { screen } = await renderApp('/sign-up')
    await screen.getByLabelText('Email').fill('linus@example.com')
    await screen.getByLabelText('Password').fill('correct horse battery')
    api.failNext('POST', /^\/v1\/console\/account$/, 403, 'signup_closed', 'Closed.')
    await userEvent.click(submit())
    await expect.poll(text).toContain(inviteOnly)
  })

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode`, async () => {
      setMode(theme, 'comfortable')
      const { screen } = await renderApp('/sign-up')
      await expect.element(screen.getByLabelText('Password')).toBeVisible()
      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    })
  }
})
