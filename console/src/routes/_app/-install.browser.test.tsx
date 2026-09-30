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
  api.installSmtp = null
  api.installEmails = []
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

  // Spec 0009, AC-7: the install's email server, the same form as a project's Settings tab.
  it('saves the email server, then offers to remove it', async () => {
    const { screen } = await renderApp('/install')
    await expect.poll(text).toContain('Email server')
    await expect.poll(() => document.getElementById('install-smtp-host')).not.toBeNull()
    expect(button('Remove email server')).toBeUndefined()

    await screen.getByLabelText('Host').fill('smtp.example.com')
    await screen.getByRole('radio', { name: 'TLS (usually port 465)' }).click()
    await screen.getByLabelText('From email').fill('orvano@example.com')
    // The second Save: the first belongs to the Console sign up card.
    const save = [...document.querySelectorAll<HTMLButtonElement>('button')]
      .filter((element) => element.textContent === 'Save')
      .at(1)
    await userEvent.click(save ?? document.body)

    await expect.poll(text).toContain('Email server saved')
    expect(api.requests.find((request) => request.method === 'PUT')?.body).toEqual({
      host: 'smtp.example.com',
      port: 465,
      security: 'tls',
      username: null,
      password: null,
      fromEmail: 'orvano@example.com',
      fromName: null,
      replyTo: null,
    })

    await userEvent.click(button('Remove email server') ?? document.body)
    const confirm = () => document.querySelector('[role=alertdialog]')
    await expect
      .poll(() => confirm()?.textContent)
      .toContain('Projects without their own settings won’t be able to send email.')
    const remove = [...(confirm()?.querySelectorAll('button') ?? [])].find(
      (element) => element.textContent === 'Remove email server',
    )
    await userEvent.click(remove ?? document.body)
    await expect.poll(text).toContain('Email server removed')
    expect(api.installSmtp).toBeNull()
  })

  // Spec 0009, AC-21: the console's own email log, with the reason in plain words.
  it('lists the console emails with their status and reason', async () => {
    api.installEmails = [
      {
        id: 'email000000000000001',
        template: 'console_invitation',
        recipient: 'g***@example.com',
        status: 'failed',
        smtpSource: null,
        attempts: 6,
        errorCode: 'smtp_unreachable',
        createdAt: '2026-06-01T10:00:00.000Z',
        completedAt: '2026-06-01T10:16:00.000Z',
      },
    ]
    await renderApp('/install')
    await expect.poll(text).toContain('g***@example.com')
    expect(text()).toContain('Console invite')
    expect(text()).toContain('Failed')
    expect(text()).toContain('Couldn’t connect to the SMTP server.')
  })

  it('says so when no email was sent in the last 30 days', async () => {
    await renderApp('/install')
    await expect.poll(text).toContain('No emails in the last 30 days')
  })

  it.each(['dark', 'light'] as const)('has no axe violations in the %s theme', async (theme) => {
    setMode(theme, 'comfortable')
    await renderApp('/install')
    await expect.poll(text).toContain('Console sign up')
    await expect.poll(text).toContain('No emails in the last 30 days')
    await expect.poll(() => document.getElementById('install-smtp-host')).not.toBeNull()
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
