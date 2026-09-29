import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

import { todayLocal } from './expiry'

// Spec 0007, AC-10 and AC-13 to AC-15: the Create key form, what it sends, the reveal step that
// shows the secret once, and where the secret must never end up.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'
/** What the fake API answers as the new key's secret. */
const secret = 'orv_fake_secret_for_tests_only'
const dayMs = 86_400_000

function seed(role: OrgRole = 'owner') {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001' })]
  api.apiKeys = []
  api.requests = []
}

const text = () => document.body.textContent
const dialog = () => document.querySelector('[role=dialog]')
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const posts = () =>
  api.requests.filter(
    (request) => request.method === 'POST' && request.path === '/v1/console/project/keys',
  )
const sentBody = () => posts()[0]?.body as Record<string, unknown> | undefined
/** Every key and value in `storage`, as one string. */
const stored = (storage: Storage) =>
  Array.from({ length: storage.length }, (_, i) => {
    const key = storage.key(i) ?? ''
    return `${key}=${storage.getItem(key) ?? ''}`
  }).join('\n')
const settle = () => new Promise((resolve) => setTimeout(resolve, 300))

async function openCreateKey(role: OrgRole = 'owner') {
  seed(role)
  const app = await renderApp(`/projects/${projectId}/keys`)
  await expect.poll(text).toContain('No API keys yet')
  await userEvent.click(button('Create key') ?? document.body)
  await expect.poll(() => document.querySelector('#create-key-name')).not.toBeNull()
  return app
}

async function chooseExpiry(label: string) {
  const option = () =>
    [...document.querySelectorAll('[role=option]')].find((item) => item.textContent === label)
  await userEvent.click(document.querySelector('#create-key-expiry') ?? document.body)
  await expect.poll(option).toBeDefined()
  await userEvent.click(option() ?? document.body)
}

async function fillAndCreate(name: string, scope = 'Users read') {
  await userEvent.fill(document.querySelector('#create-key-name') ?? document.body, name)
  await userEvent.click(document.querySelector(`[aria-label="${scope}"]`) ?? document.body)
  await userEvent.click(
    [...document.querySelectorAll('[role=dialog] button[type=submit]')][0] ?? document.body,
  )
}

/** Opens the form, creates a `users.read` key, and waits for the reveal step. */
async function reachReveal() {
  const app = await openCreateKey()
  await fillAndCreate('Deploy')
  await expect.poll(() => dialog()?.textContent).toContain('Copy your API key')
  return app
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the Create key form (AC-13)', () => {
  it('asks for a name and at least one scope, and sends nothing until it has them', async () => {
    await openCreateKey()
    await userEvent.click(
      [...document.querySelectorAll('[role=dialog] button[type=submit]')][0] ?? document.body,
    )
    await expect.poll(() => dialog()?.textContent).toContain('Enter a name.')
    expect(dialog()?.textContent).toContain('Choose at least one scope')
    expect(posts()).toEqual([])
  })

  it('refuses a name over 100 characters', async () => {
    await openCreateKey()
    await fillAndCreate('k'.repeat(101))
    await expect.poll(() => dialog()?.textContent).toContain('Use at most 100 characters.')
    expect(posts()).toEqual([])
  })

  it('lists one row per resource with a Read and a Write checkbox, each described', async () => {
    await openCreateKey()
    const read = document.querySelector('[aria-label="Users read"]')
    const write = document.querySelector('[aria-label="Users write"]')
    expect(read).not.toBeNull()
    expect(write).not.toBeNull()
    const description = document.getElementById(read?.getAttribute('aria-describedby') ?? '')
    expect(description?.textContent).toBe("Read a project's users.")
  })

  it('defaults Expiry to Never and sends no expiresAt for it', async () => {
    await openCreateKey()
    expect(document.querySelector('#create-key-expiry')?.textContent).toContain('Never')
    await fillAndCreate('  Deploy  ')
    await expect.poll(posts).toHaveLength(1)
    expect(sentBody()).toEqual({ name: 'Deploy', scopes: ['users.read'] })
  })

  it('sends now plus exactly 30 days of 24 hours for "30 days" (Value sourcing: N days)', async () => {
    await openCreateKey()
    await chooseExpiry('30 days')
    const before = Date.now()
    await fillAndCreate('Deploy')
    await expect.poll(posts).toHaveLength(1)
    const after = Date.now()
    const sent = Date.parse(String(sentBody()?.expiresAt))
    expect(sent).toBeGreaterThanOrEqual(before + 30 * dayMs)
    expect(sent).toBeLessThanOrEqual(after + 30 * dayMs)
    expect(String(sentBody()?.expiresAt)).toMatch(/Z$/)
  })

  it('reveals a date field for a custom date whose earliest choice is today', async () => {
    await openCreateKey()
    expect(document.querySelector('#create-key-date')).toBeNull()
    await chooseExpiry('Custom date')
    await expect.poll(() => document.querySelector('#create-key-date')).not.toBeNull()
    expect(document.querySelector<HTMLInputElement>('#create-key-date')?.min).toBe(todayLocal())
  })

  it('refuses a missing or past custom date without sending', async () => {
    await openCreateKey()
    await chooseExpiry('Custom date')
    await fillAndCreate('Deploy')
    await expect.poll(() => dialog()?.textContent).toContain('Choose a date.')
    await userEvent.fill(document.querySelector('#create-key-date') ?? document.body, '2020-01-01')
    await userEvent.click(
      [...document.querySelectorAll('[role=dialog] button[type=submit]')][0] ?? document.body,
    )
    await expect.poll(() => dialog()?.textContent).toContain('Choose today or a later date.')
    expect(posts()).toEqual([])
  })

  it('ends a custom date at 23:59:59.999 local time (Value sourcing: custom date)', async () => {
    await openCreateKey()
    await chooseExpiry('Custom date')
    await userEvent.fill(document.querySelector('#create-key-date') ?? document.body, '2099-10-05')
    await fillAndCreate('Deploy')
    await expect.poll(posts).toHaveLength(1)
    expect(sentBody()?.expiresAt).toBe(new Date(2099, 9, 5, 23, 59, 59, 999).toISOString())
  })

  it('keeps the form open with the server message when the API refuses', async () => {
    await openCreateKey()
    api.failNext('POST', /\/project\/keys$/, 400, 'validation_failed', 'That name is taken.')
    await fillAndCreate('Deploy')
    await expect
      .poll(() => dialog()?.querySelector('[role=alert]')?.textContent)
      .toContain('That name is taken.')
    expect(dialog()?.textContent).toContain("Couldn't create the key")
    expect(document.querySelector('#create-key-name')).not.toBeNull()
  })

  it('sends one request when Create key is double clicked (AC-10)', async () => {
    await openCreateKey()
    await userEvent.fill(document.querySelector('#create-key-name') ?? document.body, 'Deploy')
    await userEvent.click(document.querySelector('[aria-label="Users read"]') ?? document.body)
    await userEvent.dblClick(
      [...document.querySelectorAll('[role=dialog] button[type=submit]')][0] ?? document.body,
    )
    await expect.poll(() => dialog()?.textContent).toContain('Copy your API key')
    await settle()
    expect(posts()).toHaveLength(1)
  })
})

describe('the reveal step (AC-14)', () => {
  it('shows the secret and the URL’s project ID, and moves focus to the secret’s copy button', async () => {
    await reachReveal()
    expect(dialog()?.textContent).toContain(secret)
    expect(dialog()?.textContent).toContain(projectId)
    expect(dialog()?.textContent).toContain("This key won't be shown again")
    await expect.poll(() => document.activeElement?.getAttribute('aria-label')).toBe('Copy API key')
  })

  it('has no close button, and ignores Escape and a click outside', async () => {
    await reachReveal()
    expect(button('Close')).toBeUndefined()
    await userEvent.keyboard('{Escape}')
    await settle()
    expect(dialog()?.textContent).toContain('Copy your API key')
    const overlay = document.querySelector<HTMLElement>('[data-slot=dialog-overlay]')
    if (overlay !== null) await userEvent.click(overlay, { position: { x: 5, y: 5 }, force: true })
    await settle()
    expect(dialog()?.textContent).toContain('Copy your API key')
  })

  it('keeps Done disabled, with its reason, until you confirm you copied the key', async () => {
    await reachReveal()
    const done = button('Done')
    expect(done?.getAttribute('aria-disabled')).toBe('true')
    expect(reason(done)).toBe("Check the box to confirm you've copied it")
    await userEvent.click(document.querySelector('[role=dialog] [role=checkbox]') ?? document.body)
    await expect.poll(() => button('Done')?.getAttribute('aria-disabled')).toBeNull()
  })

  it('closes on Done and lists the new key (AC-14)', async () => {
    await reachReveal()
    await userEvent.click(document.querySelector('[role=dialog] [role=checkbox]') ?? document.body)
    await userEvent.click(button('Done') ?? document.body)
    await expect.poll(dialog).toBeNull()
    await expect.poll(() => document.querySelector('tbody')?.textContent).toContain('Deploy')
  })
})

describe('the secret stays in the reveal step only (AC-15)', () => {
  it('is in neither the query cache nor the mutation cache while it is shown', async () => {
    const { queryClient } = await reachReveal()
    const queries = JSON.stringify(
      queryClient
        .getQueryCache()
        .getAll()
        .map((q) => q.state.data),
    )
    const mutations = JSON.stringify(
      queryClient
        .getMutationCache()
        .getAll()
        .map((m) => [m.state.data, m.state.variables]),
    )
    expect(queries).not.toContain(secret)
    expect(mutations).not.toContain(secret)
  })

  it('leaves no trace in the page, the caches, storage, or the URL after Done', async () => {
    const { queryClient, router } = await reachReveal()
    await userEvent.click(document.querySelector('[role=dialog] [role=checkbox]') ?? document.body)
    await userEvent.click(button('Done') ?? document.body)
    await expect.poll(dialog).toBeNull()
    await expect.poll(() => document.querySelector('tbody')?.textContent).toContain('Deploy')

    expect(document.documentElement.outerHTML).not.toContain(secret)
    expect(
      JSON.stringify(
        queryClient
          .getQueryCache()
          .getAll()
          .map((q) => q.state.data),
      ),
    ).not.toContain(secret)
    expect(
      JSON.stringify(
        queryClient
          .getMutationCache()
          .getAll()
          .map((m) => m.state.data),
      ),
    ).not.toContain(secret)
    expect(stored(localStorage)).not.toContain(secret)
    expect(stored(sessionStorage)).not.toContain(secret)
    expect(router.state.location.href).not.toContain(secret)
  })

  it('starts the next Create key on an empty form, not the last secret', async () => {
    await reachReveal()
    await userEvent.click(document.querySelector('[role=dialog] [role=checkbox]') ?? document.body)
    await userEvent.click(button('Done') ?? document.body)
    await expect.poll(dialog).toBeNull()
    await userEvent.click(button('Create key') ?? document.body)
    await expect.poll(() => document.querySelector('#create-key-name')).not.toBeNull()
    expect(document.querySelector<HTMLInputElement>('#create-key-name')?.value).toBe('')
    expect(dialog()?.textContent).not.toContain(secret)
  })
})

describe('Create key accessibility (AC-22)', () => {
  it.each(['dark', 'light'] as const)(
    'has no axe violations on the form and the reveal step, %s theme',
    async (theme) => {
      setMode(theme, 'compact')
      await openCreateKey()
      await settle()
      expect((await axe.run(document.body)).violations.map((v) => v.id)).toEqual([])
      await fillAndCreate('Deploy')
      await expect.poll(() => dialog()?.textContent).toContain('Copy your API key')
      // The pointer still rests where Create key was, which is now Done: leave, so its reason
      // tooltip closes.
      await userEvent.unhover(button('Done') ?? document.body)
      await settle()
      expect((await axe.run(document.body)).violations.map((v) => v.id)).toEqual([])
    },
  )

  it('returns focus to Create key when the form is cancelled', async () => {
    await openCreateKey()
    const opener = button('Create key')
    await userEvent.keyboard('{Escape}')
    await expect.poll(dialog).toBeNull()
    await expect.poll(() => document.activeElement).toBe(opener)
  })
})
