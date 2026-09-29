import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole, OrgStatus } from '@orvano/console-client'

// Spec 0007, AC-1 and AC-6: Create org and Create project, from the page headers, the empty state,
// and the switcher footers, and where each one lands you.

const api: FakeApi = installFakeApi()
// Unlike the IDs the fake makes (`org000…1`, `proj000…1`), so a created org never collides.
const orgA = 'orgacme0000000000001'
const orgB = 'orgbeta0000000000002'
const projectA = 'projadmin00000000001'

function seed(role: OrgRole = 'owner', status: OrgStatus = 'active') {
  api.orgs = [makeOrg({ id: orgA, name: 'Acme', role, status })]
  api.projects = []
  api.requests = []
}

const text = () => document.body.textContent
const heading = () => document.querySelector('h1#page-title')?.textContent
const dialog = () => document.querySelector('[data-slot=dialog-content]')
const buttons = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].filter(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const trigger = (label: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find((element) =>
    element.getAttribute('aria-label')?.startsWith(label),
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const sent = (method: string, path: RegExp) =>
  api.requests.filter((request) => request.method === method && path.test(request.path))
const submit = () =>
  userEvent.click(dialog()?.querySelector('button[type=submit]') ?? document.body)
const settle = () => new Promise((resolve) => setTimeout(resolve, 300))

/** Opens a switcher's list and presses the action in its footer. */
async function pressFooter(switcher: string) {
  await expect.poll(() => trigger(switcher)).toBeDefined()
  await userEvent.click(trigger(switcher) ?? document.body)
  const footer = () =>
    document.querySelector<HTMLButtonElement>('[data-slot=switcher-footer] button')
  await expect.poll(footer).not.toBeNull()
  await userEvent.click(footer() ?? document.body)
}

async function openCreateOrg() {
  seed()
  const app = await renderApp('/orgs')
  await expect.poll(heading).toBe('Orgs')
  await userEvent.click(buttons('Create org')[0] ?? document.body)
  await expect.poll(() => document.querySelector('#create-org-name')).not.toBeNull()
  return app
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('Create org (AC-1)', () => {
  it('trims the name, confirms, and lands on the new org, with the switcher showing it', async () => {
    const { router } = await openCreateOrg()
    await userEvent.fill(document.querySelector('#create-org-name') ?? document.body, '  Beta  ')
    await submit()
    await expect.poll(text).toContain('Org created')
    expect(sent('POST', /^\/v1\/console\/orgs$/).map((request) => request.body)).toEqual([
      { name: 'Beta' },
    ])
    const created = api.orgs.find((org) => org.name === 'Beta')
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${created?.id ?? ''}`)
    await expect.poll(dialog).toBeNull()
    await expect
      .poll(() => trigger('Switch org')?.getAttribute('aria-label'))
      .toBe('Switch org: Beta')
  })

  it('lands on the ID the server answered, even when two orgs share a name (Value sourcing)', async () => {
    const { router } = await openCreateOrg()
    await userEvent.fill(document.querySelector('#create-org-name') ?? document.body, 'Acme')
    await submit()
    const newest = () => api.orgs.at(-1)?.id
    await expect.poll(() => router.state.location.pathname).toBe(`/orgs/${newest() ?? ''}`)
    expect(newest()).not.toBe(orgA)
  })

  it.each([
    ['an empty name', '   ', 'Enter a name.'],
    ['a name over 100 characters', 'a'.repeat(101), 'Use at most 100 characters.'],
  ])('shows a field error for %s and sends nothing', async (_, name, message) => {
    await openCreateOrg()
    await userEvent.fill(document.querySelector('#create-org-name') ?? document.body, name)
    await submit()
    await expect
      .poll(() => document.getElementById('create-org-name-error')?.textContent)
      .toBe(message)
    expect(document.querySelector('#create-org-name')?.getAttribute('aria-invalid')).toBe('true')
    expect(sent('POST', /^\/v1\/console\/orgs$/)).toEqual([])
  })

  it('shows a server error in the form alert and stays open', async () => {
    await openCreateOrg()
    api.failNext('POST', /^\/v1\/console\/orgs$/, 400, 'validation_failed', 'Name refused.')
    await userEvent.fill(document.querySelector('#create-org-name') ?? document.body, 'Beta')
    await submit()
    await expect
      .poll(() => dialog()?.querySelector('[role=alert]')?.textContent)
      .toContain('Name refused.')
    expect(dialog()?.textContent).toContain("Couldn't create the org")
  })

  it('opens from the org switcher footer with focus on Name, and Escape returns to the switcher', async () => {
    seed()
    await renderApp(`/orgs/${orgA}`)
    await expect.poll(heading).toBe('Projects')
    await pressFooter('Switch org')
    await expect.poll(() => document.activeElement?.id).toBe('create-org-name')
    await userEvent.keyboard('{Escape}')
    await expect.poll(dialog).toBeNull()
    await expect.poll(() => document.activeElement).toBe(trigger('Switch org'))
  })
})

describe('Create project (AC-6)', () => {
  it('sits in the org page header and its empty state', async () => {
    seed()
    await renderApp(`/orgs/${orgA}`)
    await expect.poll(text).toContain('No projects yet')
    expect(buttons('Create project')).toHaveLength(2)
    expect(document.querySelector('[data-slot=empty-action]')?.textContent).toBe('Create project')
  })

  it('creates the project, confirms, and lands on it while it is set up', async () => {
    seed()
    const { router } = await renderApp(`/orgs/${orgA}`)
    await expect.poll(text).toContain('No projects yet')
    await userEvent.click(buttons('Create project')[0] ?? document.body)
    await expect.poll(() => document.querySelector('#create-project-name')).not.toBeNull()
    await userEvent.fill(
      document.querySelector('#create-project-name') ?? document.body,
      '  Storefront  ',
    )
    await submit()
    await expect.poll(text).toContain('Project created')
    expect(sent('POST', /\/projects$/).map((request) => [request.path, request.body])).toEqual([
      [`/v1/console/orgs/${orgA}/projects`, { name: 'Storefront' }],
    ])
    const created = api.projects.find((project) => project.name === 'Storefront')
    await expect.poll(() => router.state.location.pathname).toBe(`/projects/${created?.id ?? ''}`)
    await expect.poll(heading).toBe('Setting up')
  })

  it('is disabled for a viewer with the role reason', async () => {
    seed('viewer')
    await renderApp(`/orgs/${orgA}`)
    await expect.poll(text).toContain('No projects yet')
    for (const element of buttons('Create project')) {
      expect(element.getAttribute('aria-disabled')).toBe('true')
      expect(reason(element)).toBe('Developers and owners only')
    }
  })

  it('is disabled while the org is being deleted', async () => {
    seed('owner', 'deleting')
    await renderApp(`/orgs/${orgA}`)
    await expect.poll(text).toContain('No projects yet')
    await expect.poll(() => reason(buttons('Create project')[0])).toBe('Restore the org first')
  })

  it("creates it in the current project's org from the project switcher, not the last org visited", async () => {
    seed()
    api.orgs.push(makeOrg({ id: orgB, name: 'Beta' }))
    api.projects = [makeProject({ id: projectA, orgId: orgA })]
    const { router } = await renderApp(`/orgs/${orgB}`)
    await expect.poll(heading).toBe('Projects')
    await router.navigate({ to: '/projects/$projectId', params: { projectId: projectA } })
    await expect.poll(() => trigger('Switch project')).toBeDefined()
    await pressFooter('Switch project')
    await expect.poll(() => document.querySelector('#create-project-name')).not.toBeNull()
    await userEvent.fill(document.querySelector('#create-project-name') ?? document.body, 'Admin')
    await submit()
    await expect
      .poll(() => sent('POST', /\/projects$/).map((r) => r.path))
      .toEqual([`/v1/console/orgs/${orgA}/projects`])
  })
})

describe('Create org and Create project accessibility (AC-22)', () => {
  it.each(['dark', 'light'] as const)(
    'has no axe violations with Create org open, %s theme',
    async (theme) => {
      setMode(theme, 'comfortable')
      await openCreateOrg()
      await settle()
      const results = await axe.run(document.body)
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )

  it('keeps Tab inside the dialog', async () => {
    await openCreateOrg()
    // Start once the dialog has moved focus inside; a Tab before that starts from the page.
    await expect.poll(() => document.activeElement?.id).toBe('create-org-name')
    for (let i = 0; i < 6; i++) {
      await userEvent.tab()
      await expect.poll(() => dialog()?.contains(document.activeElement)).toBe(true)
    }
  })
})
