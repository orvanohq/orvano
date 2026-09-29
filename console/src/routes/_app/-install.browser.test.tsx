import axe from 'axe-core'
import { beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, type FakeApi } from '@/test/fake-api'

// Spec 0008, AC-24 and AC-26: Install settings, for install admins, and its account menu item.

const api: FakeApi = installFakeApi()

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.textContent === name || element.getAttribute('aria-label') === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}

beforeEach(() => {
  setMode('dark', 'compact')
  api.account = { ...api.account, isInstallAdmin: true }
  api.consoleSignup = 'invite'
  api.orgs = []
  api.requests = []
})

describe('/install', () => {
  it('switches console sign up once the choice changes', async () => {
    const { screen } = await renderApp('/install')
    await expect.poll(text).toContain('Console sign up')
    await expect.poll(() => document.title).toBe('Install settings · Orvano')
    expect(reason(button('Save'))).toBe('Choose a different setting')
    await screen.getByRole('radio', { name: 'Anyone can sign up' }).click()
    expect(text()).toContain('Anyone who can reach this console can create an account')
    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(text).toContain('Sign up updated')
    expect(api.consoleSignup).toBe('open')
    expect(api.requests.find((request) => request.method === 'PATCH')?.body).toEqual({
      consoleSignup: 'open',
    })
  })

  it('shows the in shell not found screen to anyone who is not an install admin', async () => {
    api.account = { ...api.account, isInstallAdmin: false }
    await renderApp('/install')
    await expect.poll(text).toContain('Page not found')
    expect(api.requests.some((request) => request.path === '/v1/console/install/settings')).toBe(
      false,
    )
  })

  it('has no org or project sidebar', async () => {
    await renderApp('/install')
    await expect.poll(text).toContain('Console sign up')
    expect(document.querySelector('nav[aria-label="Org navigation"]')).toBeNull()
  })

  it.each(['dark', 'light'] as const)('has no axe violations in the %s theme', async (theme) => {
    setMode(theme, 'comfortable')
    await renderApp('/install')
    await expect.poll(text).toContain('Console sign up')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})

describe('the account menu (AC-24)', () => {
  async function menuItems() {
    await userEvent.click(button('Account menu') ?? document.body)
    await expect.poll(() => document.querySelector('[role=menu]')).not.toBeNull()
    return [...document.querySelectorAll('[role=menuitem]')].map((item) => item.textContent)
  }

  it('shows Install settings above Sign out for install admins, and opens /install', async () => {
    const { router } = await renderApp('/orgs')
    await expect.poll(() => button('Account menu')).toBeDefined()
    const items = await menuItems()
    expect(items.slice(-2)).toEqual(['Install settings', 'Sign out'])
    const item = [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(
      (element) => element.textContent === 'Install settings',
    )
    await userEvent.click(item ?? document.body)
    await expect.poll(() => router.state.location.pathname).toBe('/install')
  })

  it('leaves it out for everyone else', async () => {
    api.account = { ...api.account, isInstallAdmin: false }
    await renderApp('/orgs')
    await expect.poll(() => button('Account menu')).toBeDefined()
    expect(await menuItems()).not.toContain('Install settings')
  })
})
