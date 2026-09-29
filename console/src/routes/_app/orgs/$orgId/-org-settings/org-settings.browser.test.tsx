import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { formatFull } from '@/lib/format'
import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole, ProjectStatus } from '@orvano/console-client'

// Spec 0007, AC-2 to AC-5: the org Settings page, its sidebar entry, rename, delete with the
// project check, and Restore org.

const api: FakeApi = installFakeApi()
const orgId = 'org00000000000000001'

// Holds the org's project list while `projectsHeld` is set, to see Delete org before its check ran.
// Installed before the app loads, so the console client picks it up.
let projectsHeld: Promise<void> | null = null
const fakeFetch = globalThis.fetch
globalThis.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = new URL(input instanceof Request ? input.url : String(input), window.location.href)
  if (projectsHeld !== null && /^\/v1\/console\/orgs\/[^/]+\/projects$/.test(url.pathname)) {
    await projectsHeld
  }
  return fakeFetch(input, init)
}

function seed(role: OrgRole, projects: ProjectStatus[] = []) {
  api.orgs = [makeOrg({ id: orgId, role })]
  api.projects = projects.map((status, i) =>
    makeProject({ id: `proj000000000000000${String(i + 1)}`, orgId, status }),
  )
  api.requests = []
}

const text = () => document.body.textContent
const heading = () => document.querySelector('h1#page-title')?.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].filter(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const sidebarLinks = () =>
  [...document.querySelectorAll('nav a')].map((link) => link.textContent.trim())

async function openSettings(role: OrgRole, projects: ProjectStatus[] = []) {
  seed(role, projects)
  await renderApp(`/orgs/${orgId}/settings`)
  await expect.poll(heading).toBe('Settings')
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the org Settings entry and page (AC-2)', () => {
  it('shows Settings in the org sidebar to owners only', async () => {
    seed('owner')
    await renderApp(`/orgs/${orgId}`)
    await expect.poll(sidebarLinks).toContain('Settings')
  })

  it('hides the entry from a developer, who sees the page read only at its URL', async () => {
    await openSettings('developer')
    await expect.poll(sidebarLinks).toContain('Projects')
    expect(sidebarLinks()).not.toContain('Settings')
    expect(document.querySelector<HTMLInputElement>('#org-name')?.readOnly).toBe(true)
    expect(reason(button('Save')[0])).toBe('Owners only')
    expect(reason(button('Delete org')[0])).toBe('Owners only')
  })

  it('shows the org ID and created date', async () => {
    await openSettings('owner')
    expect(text()).toContain(orgId)
    expect(document.querySelector('time')?.getAttribute('datetime')).toBe(
      '2026-06-01T10:00:00.000Z',
    )
    expect(document.title).toBe('Settings · Acme · Orvano')
  })
})

describe('renaming the org (AC-3)', () => {
  it('updates the heading, title, and switcher without a reload', async () => {
    await openSettings('owner')
    await userEvent.fill(document.querySelector('#org-name') ?? document.body, 'Acme Labs')
    await userEvent.click(button('Save')[0] ?? document.body)
    await expect.poll(() => document.title).toBe('Settings · Acme Labs · Orvano')
    await expect.poll(() => document.querySelector('header')?.textContent).toContain('Acme Labs')
    expect(api.requests.find((r) => r.method === 'PATCH')?.body).toEqual({ name: 'Acme Labs' })
  })
})

describe('deleting the org (AC-4)', () => {
  it('says "Checking projects…" until the first page of projects has loaded', async () => {
    let release: (() => void) | undefined
    projectsHeld = new Promise((resolve) => {
      release = resolve
    })
    try {
      await openSettings('owner', ['active'])
      await expect.poll(() => reason(button('Delete org')[0])).toBe('Checking projects…')
      expect(button('Delete org')[0]?.getAttribute('aria-disabled')).toBe('true')
    } finally {
      release?.()
      projectsHeld = null
    }
    await expect.poll(() => reason(button('Delete org')[0])).toBe('Delete its projects first')
  })

  it('is blocked while a project is live', async () => {
    await openSettings('owner', ['active', 'deleting'])
    await expect.poll(() => reason(button('Delete org')[0])).toBe('Delete its projects first')
  })

  it('shows the server refusal in the dialog and stays open', async () => {
    await openSettings('owner', ['deleting'])
    api.failNext('DELETE', /\/orgs\//, 409, 'org_not_empty', 'This org still has projects.')
    await expect.poll(() => button('Delete org')[0]?.getAttribute('aria-disabled')).toBeNull()
    await userEvent.click(button('Delete org')[0] ?? document.body)
    await expect.poll(() => document.activeElement?.textContent).toBe('Cancel')
    await userEvent.click(
      document.querySelector('[data-slot=alert-dialog-action]') ?? document.body,
    )
    await expect
      .poll(() => document.querySelector('[role=alertdialog] [role=alert]')?.textContent)
      .toContain('This org still has projects.')
  })

  it('deletes an org with no live project, then shows the banner and restores it (AC-5)', async () => {
    await openSettings('owner', ['deleting'])
    await expect.poll(() => button('Delete org')[0]?.getAttribute('aria-disabled')).toBeNull()
    await userEvent.click(button('Delete org')[0] ?? document.body)
    await userEvent.click(
      document.querySelector('[data-slot=alert-dialog-action]') ?? document.body,
    )
    await expect.poll(text).toContain('Org deleted')
    await expect
      .poll(text)
      .toContain(`is purged for good on ${formatFull('2026-07-01T10:00:00.000Z')}`)
    // One in the banner, one in the Danger zone.
    await expect.poll(() => button('Restore org').length).toBe(2)
    expect(document.querySelector<HTMLInputElement>('#org-name')?.readOnly).toBe(true)
    expect(text()).toContain('Restore the org to rename it')

    await userEvent.click(button('Restore org')[0] ?? document.body)
    await expect.poll(text).toContain('Org restored')
    await expect.poll(() => button('Restore org').length).toBe(0)
    expect(api.requests.map((r) => `${r.method} ${r.path}`)).toContain(
      `POST /v1/console/orgs/${orgId}/restore`,
    )
  })

  it('disables Restore org for a non owner', async () => {
    seed('developer')
    api.orgs = [makeOrg({ id: orgId, role: 'developer', status: 'deleting' })]
    await renderApp(`/orgs/${orgId}`)
    await expect.poll(() => button('Restore org').length).toBe(1)
    expect(reason(button('Restore org')[0])).toBe('Owners only')
  })
})

describe('org Settings and accessibility (AC-22)', () => {
  it.each([
    ['dark', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'compact'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in the %s theme, %s density', async (theme, density) => {
    setMode(theme, density)
    await openSettings('owner', ['active'])
    await expect.poll(() => reason(button('Delete org')[0])).toBe('Delete its projects first')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })

  it.each(['dark', 'light'] as const)(
    'has no axe violations with the Deleting banner, %s theme',
    async (theme) => {
      setMode(theme, 'compact')
      seed('owner')
      api.orgs = [
        makeOrg({
          id: orgId,
          status: 'deleting',
          purgeAfter: '2026-07-01T10:00:00.000Z',
        }),
      ]
      await renderApp(`/orgs/${orgId}/settings`)
      await expect.poll(() => button('Restore org').length).toBe(2)
      const results = await axe.run(document.body)
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )
})
