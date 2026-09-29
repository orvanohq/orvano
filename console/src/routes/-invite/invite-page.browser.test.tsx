import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, type FakeApi } from '@/test/fake-api'
import type { InvitationPreview } from '@orvano/console-client'

// Spec 0008, AC-20 to AC-22 and AC-26: the public invite page, /invite#<token>.

const api: FakeApi = installFakeApi()
const token = 'inviteInviteInviteInviteInviteInviteInvit0'
const testPage = window.location.pathname + window.location.search
const preview: InvitationPreview = {
  orgId: 'orgi0000000000000001',
  orgName: 'Acme',
  role: 'developer',
  email: 'grace@example.com',
  invitedByName: 'Ada Lovelace',
  expiresAt: '2026-06-08T10:00:00.000Z',
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.textContent === name,
  )
const field = (id: string) => document.getElementById(id) ?? document.body
/** The submit button of the form holding `fieldId`, since the tabs share their names. */
const submitOf = (fieldId: string) =>
  document
    .getElementById(fieldId)
    ?.closest('form')
    ?.querySelector<HTMLButtonElement>('button[type=submit]') ?? document.body
const sent = (method: string, path: string) =>
  api.requests.filter((request) => request.method === method && request.path === path)

/** Opens the page as the link does: the token in the real address bar's fragment. */
async function openInvite(withToken = true) {
  window.history.replaceState(null, '', withToken ? `/invite#${token}` : '/invite')
  return renderApp('/invite')
}

beforeEach(() => {
  setMode('dark', 'compact')
  api.orgs = []
  api.preview = { ...preview }
  api.signedIn = false
  api.account = { ...api.account, email: 'grace@example.com' }
  api.requests = []
})

afterEach(() => {
  window.history.replaceState(null, '', testPage)
  setMode('dark', 'compact')
})

// First in the file: the route keeps the token it read last, as it must across its own reload.
it('says the link is incomplete when it has no token', async () => {
  await openInvite(false)
  await expect.poll(text).toContain('This invite link is incomplete')
  expect(sent('POST', '/v1/console/invitations/preview')).toHaveLength(0)
})

describe('the token (AC-20)', () => {
  it('leaves the address bar and never enters a query key or cache', async () => {
    const { queryClient } = await openInvite()
    await expect.poll(text).toContain('invited you to join')
    expect(window.location.hash).toBe('')
    expect(sent('POST', '/v1/console/invitations/preview')[0]?.body).toEqual({ token })
    const cache = JSON.stringify(
      queryClient
        .getQueryCache()
        .getAll()
        .map((query) => [query.queryKey, query.state.data]),
    )
    expect(cache).not.toContain(token)
    await expect.poll(() => document.title).toBe('Join Acme · Orvano')
  })

  it.each([
    [404, 'invitation_not_found', "This invite link isn't valid anymore"],
    [410, 'invitation_expired', 'This invite expired'],
    [409, 'org_not_active', 'This org is being deleted'],
  ] as const)('explains a %s answer', async (status, code, message) => {
    api.failNext('POST', /\/invitations\/preview$/, status, code, 'raw')
    await openInvite()
    await expect.poll(text).toContain(message)
    expect(text()).not.toContain('raw')
  })

  it('shows the error panel with Retry for anything else', async () => {
    api.failNext('POST', /\/invitations\/preview$/, 500, 'internal_error', 'Boom.')
    await openInvite()
    await expect.poll(text).toContain("This page didn't load")
    await userEvent.click(button('Retry') ?? document.body)
    await expect.poll(text).toContain('invited you to join')
  })

  it('previews a second link pasted into the open tab', async () => {
    const { router } = await openInvite()
    await expect.poll(text).toContain('invited you to join Acme')
    api.preview = { ...preview, orgId: 'orgi0000000000000002', orgName: 'Globex' }
    const second = 'secondSecondSecondSecondSecondSecondSecon0'
    // The test router's history is in memory; the browser's would load the route again on popstate.
    window.history.replaceState(null, '', `/invite#${second}`)
    await router.invalidate()
    await expect.poll(text).toContain('invited you to join Globex')
    expect(window.location.hash).toBe('')
    expect(sent('POST', '/v1/console/invitations/preview').at(-1)?.body).toEqual({ token: second })
  })
})

describe('signed out (AC-22)', () => {
  it('stays on the page with Create account first, the email filled in and read only', async () => {
    const { router } = await openInvite()
    await expect.poll(text).toContain('Ada Lovelace invited you to join Acme as developer.')
    expect(router.state.location.pathname).toBe('/invite')
    const tabs = [...document.querySelectorAll('[role=tab]')].map((tab) => tab.textContent)
    expect(tabs).toEqual(['Create account', 'Sign in'])
    const email = document.querySelector<HTMLInputElement>('#invite-sign-up-email')
    expect(email?.value).toBe('grace@example.com')
    expect(email?.readOnly).toBe(true)
  })

  it('creates the account with the token and lands on the inviting org', async () => {
    const { router } = await openInvite()
    await expect.poll(text).toContain('invited you to join')
    await userEvent.fill(field('invite-sign-up-password'), 'correct horse battery')
    await userEvent.click(submitOf('invite-sign-up-password'))
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${preview.orgId}`)
    await expect.poll(text).toContain('You joined Acme')
    expect(sent('POST', '/v1/console/account')[0]?.body).toMatchObject({
      email: 'grace@example.com',
      inviteToken: token,
      name: null,
    })
  })

  it('offers Sign in when the account already exists', async () => {
    await openInvite()
    await expect.poll(text).toContain('invited you to join')
    api.failNext('POST', /^\/v1\/console\/account$/, 409, 'user_already_exists', 'Exists.')
    await userEvent.fill(field('invite-sign-up-password'), 'correct horse battery')
    await userEvent.click(submitOf('invite-sign-up-password'))
    await expect.poll(() => button('Sign in instead')).toBeDefined()
    await userEvent.click(button('Sign in instead') ?? document.body)
    await expect
      .poll(() => document.querySelector('[role=tab][aria-selected=true]')?.textContent)
      .toBe('Sign in')
    expect(document.querySelector<HTMLInputElement>('#invite-sign-in-email')?.value).toBe(
      'grace@example.com',
    )
  })

  it('shows the signed in state after signing in on the Sign in tab', async () => {
    await openInvite()
    await expect.poll(text).toContain('invited you to join')
    await userEvent.click(
      [...document.querySelectorAll<HTMLElement>('[role=tab]')].at(1) ?? document.body,
    )
    await userEvent.fill(field('invite-sign-in-password'), 'correct horse battery')
    await userEvent.click(submitOf('invite-sign-in-password'))
    await expect.poll(() => button('Join')).toBeDefined()
  })
})

describe('signed in (AC-21)', () => {
  it('joins with Join and lands on the org', async () => {
    api.signedIn = true
    const { router } = await openInvite()
    await expect.poll(() => button('Join')).toBeDefined()
    await userEvent.click(button('Join') ?? document.body)
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${preview.orgId}`)
    await expect.poll(text).toContain('You joined Acme')
    expect(sent('POST', '/v1/console/invitations/accept')[0]?.body).toEqual({ token })
  })

  it('says so when you were already a member', async () => {
    api.signedIn = true
    api.orgs = [makeOrg({ id: preview.orgId, name: 'Acme', role: 'viewer' })]
    await openInvite()
    await expect.poll(() => button('Join')).toBeDefined()
    await userEvent.click(button('Join') ?? document.body)
    await expect.poll(text).toContain("You're already a member of Acme")
  })

  it('names both emails when they differ, and Sign out keeps the invite', async () => {
    api.signedIn = true
    api.account = { ...api.account, email: 'linus@example.com' }
    await openInvite()
    await expect.poll(text).toContain('This invite is for grace@example.com.')
    expect(text()).toContain("You're signed in as linus@example.com.")
    await userEvent.click(button('Sign out') ?? document.body)
    await expect.poll(() => document.querySelectorAll('[role=tab]').length).toBe(2)
    expect(sent('POST', '/v1/console/invitations/preview').at(-1)?.body).toEqual({ token })
  })
})

describe('accessibility (AC-26)', () => {
  it.each(['dark', 'light'] as const)(
    'has no axe violations signed out, in the %s theme',
    async (theme) => {
      setMode(theme, 'comfortable')
      await openInvite()
      await expect.poll(text).toContain('invited you to join')
      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )
})
