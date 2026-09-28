import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole, OrgStatus, Project } from '@orvano/console-client'

// Spec 0007, AC-7 to AC-10: project Settings (rename, typed name delete, the Restore toast) and the
// status panels' recovery actions.

const api: FakeApi = installFakeApi()
const orgId = 'org00000000000000001'
const projectId = 'proj0000000000000001'

function seed(role: OrgRole, project: Partial<Project> = {}, orgStatus: OrgStatus = 'active') {
  api.orgs = [makeOrg({ id: orgId, role, status: orgStatus })]
  api.projects = [makeProject({ id: projectId, orgId, ...project })]
  api.apiKeys = []
  api.platforms = []
  api.requests = []
}

const text = () => document.body.textContent
const heading = () => document.querySelector('h1#page-title')?.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const sent = (method: string) =>
  api.requests.filter((request) => request.method === method).map((request) => request.path)
const click = async (name: string) => {
  await expect.poll(() => button(name)).toBeDefined()
  await userEvent.click(button(name) ?? document.body)
}

async function openSettings(role: OrgRole) {
  seed(role)
  const app = await renderApp(`/projects/${projectId}/settings`)
  await expect.poll(heading).toBe('Settings')
  await expect
    .poll(() => document.querySelector<HTMLInputElement>('#project-name')?.value)
    .toBe('Scenarios')
  return app
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('project Settings, General (AC-7)', () => {
  it('shows the name form and the project ID above the signing keys and the Danger zone', async () => {
    await openSettings('owner')
    const sections = [...document.querySelectorAll('h2')].map((h2) => h2.textContent)
    expect(sections).toEqual(['General', 'Token signing keys', 'Danger zone'])
    expect(text()).toContain(projectId)
  })

  it('renames the project and updates the title and switcher without a reload', async () => {
    await openSettings('developer')
    const field = document.querySelector<HTMLInputElement>('#project-name')
    await userEvent.fill(field ?? document.body, '  Storefront  ')
    await click('Save')
    await expect.poll(() => document.title).toBe('Settings · Storefront · Orvano')
    expect(api.requests.find((r) => r.method === 'PATCH')?.body).toEqual({ name: 'Storefront' })
    await expect.poll(() => document.querySelector('header')?.textContent).toContain('Storefront')
  })

  it('makes the name read only for a viewer and Delete project owners only', async () => {
    await openSettings('viewer')
    expect(document.querySelector<HTMLInputElement>('#project-name')?.readOnly).toBe(true)
    expect(text()).toContain('Developers and owners only')
    expect(reason(button('Save'))).toBe('Developers and owners only')
    expect(reason(button('Delete project'))).toBe('Owners only')
  })

  it('lets a developer rename but not delete', async () => {
    await openSettings('developer')
    expect(document.querySelector<HTMLInputElement>('#project-name')?.readOnly).toBe(false)
    expect(button('Delete project')?.getAttribute('aria-disabled')).toBe('true')
    expect(reason(button('Delete project'))).toBe('Owners only')
  })
})

describe('deleting a project (AC-8)', () => {
  it('needs the exact name typed, then lands on the org with a Deleting badge and a Restore toast', async () => {
    const { router } = await openSettings('owner')
    await click('Delete project')
    await expect.poll(() => document.querySelector('[role=alertdialog]')).not.toBeNull()
    const confirm = () =>
      document.querySelector<HTMLButtonElement>(
        '[role=alertdialog] [data-slot=alert-dialog-action]',
      )
    expect(confirm()?.disabled).toBe(true)
    await userEvent.fill(document.querySelector('#confirm-name') ?? document.body, 'scenarios')
    expect(confirm()?.disabled).toBe(true)
    await userEvent.fill(document.querySelector('#confirm-name') ?? document.body, 'Scenarios')
    await expect.poll(() => confirm()?.disabled).toBe(false)
    await userEvent.click(confirm() ?? document.body)

    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${orgId}`)
    await expect.poll(text).toContain('Project deleted')
    await expect.poll(() => document.querySelector('tbody')?.textContent).toContain('Deleting')

    await click('Restore')
    await expect.poll(text).toContain('Project restored')
    expect(sent('POST')).toContain('/v1/console/project/restore')
    await expect.poll(() => document.querySelector('tbody')?.textContent).toContain('Setting up')
  })

  it('keeps the dialog open and refetches when the project moved under you (AC-10)', async () => {
    await openSettings('owner')
    api.failNext('DELETE', /^\/v1\/console\/project$/, 409, 'project_not_ready', 'Not ready.')
    await click('Delete project')
    await userEvent.fill(document.querySelector('#confirm-name') ?? document.body, 'Scenarios')
    const gets = sent('GET').filter((path) => path === '/v1/console/project').length
    await userEvent.click(
      document.querySelector('[role=alertdialog] [data-slot=alert-dialog-action]') ?? document.body,
    )
    await expect.poll(text).toContain("Couldn't delete the project")
    await expect
      .poll(() => document.querySelector('[role=alertdialog] [role=alert]')?.textContent)
      .toContain('Not ready.')
    await expect
      .poll(() => sent('GET').filter((path) => path === '/v1/console/project').length)
      .toBeGreaterThan(gets)
  })
})

describe('status panel actions (AC-9)', () => {
  it('retries a failed setup and shows "Setting up"', async () => {
    seed('developer', { status: 'failed' })
    await renderApp(`/projects/${projectId}`)
    await expect.poll(heading).toBe('Setup failed')
    await click('Retry setup')
    await expect.poll(heading).toBe('Setting up')
    await expect.poll(text).toContain('Setup restarted')
    expect(sent('POST')).toEqual(['/v1/console/project/retry-provisioning'])
  })

  it('disables Retry setup for a viewer', async () => {
    seed('viewer', { status: 'failed' })
    await renderApp(`/projects/${projectId}`)
    await expect.poll(heading).toBe('Setup failed')
    expect(reason(button('Retry setup'))).toBe('Developers and owners only')
  })

  it('restores a deleted project, and disables Restore while its org is deleting', async () => {
    seed('owner', { status: 'deleting', purgeAfter: '2026-07-01T10:00:00.000Z' }, 'deleting')
    await renderApp(`/projects/${projectId}`)
    await expect.poll(heading).toBe('Being deleted')
    await expect.poll(() => reason(button('Restore project'))).toBe('Restore the org first')
  })

  it('retries a failed purge and clears the failure note', async () => {
    seed('owner', {
      status: 'deleting',
      purgeAfter: '2026-07-01T10:00:00.000Z',
      purgeFailedAt: '2026-07-02T10:00:00.000Z',
    })
    await renderApp(`/projects/${projectId}`)
    await expect.poll(text).toContain('The last purge attempt failed')
    await click('Retry purge')
    await expect.poll(() => text().includes('The last purge attempt failed')).toBe(false)
    expect(heading()).toBe('Being deleted')
    expect(button('Retry purge')).toBeUndefined()
  })

  it('offers Restore and Retry purge to owners only', async () => {
    seed('developer', {
      status: 'deleting',
      purgeAfter: '2026-07-01T10:00:00.000Z',
      purgeFailedAt: '2026-07-02T10:00:00.000Z',
    })
    await renderApp(`/projects/${projectId}`)
    await expect.poll(heading).toBe('Being deleted')
    await expect.poll(() => reason(button('Restore project'))).toBe('Owners only')
    expect(reason(button('Retry purge'))).toBe('Owners only')
  })

  it('shows an error toast and the not found view when the project is gone (AC-10)', async () => {
    seed('owner', { status: 'deleting', purgeAfter: '2026-07-01T10:00:00.000Z' })
    await renderApp(`/projects/${projectId}`)
    await expect.poll(heading).toBe('Being deleted')
    api.projects = []
    await click('Restore project')
    await expect.poll(text).toContain("Couldn't restore the project")
    await expect.poll(text).toContain('No such project.')
    await expect.poll(heading).not.toBe('Being deleted')
  })

  it.each(['dark', 'light'] as const)(
    'has no axe violations on the panels with their actions, %s theme',
    async (theme) => {
      setMode(theme, 'comfortable')
      seed('owner', {
        status: 'deleting',
        purgeAfter: '2026-07-01T10:00:00.000Z',
        purgeFailedAt: '2026-07-02T10:00:00.000Z',
      })
      await renderApp(`/projects/${projectId}`)
      await expect.poll(() => button('Retry purge')).toBeDefined()
      const results = await axe.run(document.body)
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )
})

describe('project Settings and accessibility (AC-22)', () => {
  it.each([
    ['dark', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'compact'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in the %s theme, %s density', async (theme, density) => {
    setMode(theme, density)
    await openSettings('viewer')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })

  it('has no axe violations with the typed name delete open', async () => {
    await openSettings('owner')
    await click('Delete project')
    await expect.poll(() => document.querySelector('#confirm-name')).not.toBeNull()
    await new Promise((resolve) => setTimeout(resolve, 300))
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
