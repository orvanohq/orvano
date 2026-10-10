import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, makeTable, type FakeApi } from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

// Spec 0015 build task 1: the Databases nav and page (AC-31), a database's tables with New table
// (AC-32), and a table's read only rows grid (AC-33), with titles, role gating, and axe (AC-35).

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'

function seed(role: OrgRole) {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001' })]
  api.databases = []
  api.tables = {
    main: [
      makeTable('main', { name: 'tasks', columns: [{ name: 'title', type: 'text' }] }),
      {
        ...makeTable('main', { name: 'legacy', columns: [] }),
        writable: false,
        estimatedRows: 1200,
      },
    ],
  }
  api.rows = {
    'main/tasks': [
      {
        id: '0192a000-0000-7000-8000-000000000001',
        created_at: '2026-10-10T08:30:00.123456Z',
        updated_at: '2026-10-10T08:30:00.123456Z',
        title: 'Write the spec',
      },
      {
        id: '0192a000-0000-7000-8000-000000000002',
        created_at: '2026-10-10T08:31:00.000000Z',
        updated_at: '2026-10-10T08:31:00.000000Z',
        title: null,
      },
    ],
  }
  api.requests = []
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const input = (id: string) => document.querySelector<HTMLInputElement>(`#${id}`)
const dialog = () => document.querySelector('[data-slot=dialog-content]')
const settle = () => new Promise((resolve) => setTimeout(resolve, 300))

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the Databases page (AC-31)', () => {
  it('is in the project nav after Users and its auth entries, and lists main first', async () => {
    seed('viewer')
    await renderApp(`/projects/${projectId}/databases`)
    await expect.poll(text).toContain('Active')
    const nav = [...document.querySelectorAll<HTMLAnchorElement>('nav a')].map((link) =>
      link.textContent.trim(),
    )
    expect(nav.indexOf('Databases')).toBe(nav.indexOf('Security') + 1)
    expect(nav.indexOf('Users')).toBeLessThan(nav.indexOf('Databases'))
    expect(document.querySelector('tbody tr td a')?.textContent).toBe('main')
    await expect.poll(() => document.title).toBe('Databases · Scenarios · Orvano')
  })
})

describe('a database page (AC-32)', () => {
  it('lists tables with a Read only badge and the estimate', async () => {
    seed('owner')
    await renderApp(`/projects/${projectId}/databases/main`)
    await expect.poll(text).toContain('legacy')
    const legacy = [...document.querySelectorAll('tbody tr')].find((tr) =>
      tr.textContent.includes('legacy'),
    )
    expect(legacy?.textContent).toContain('Read only')
    expect(legacy?.textContent).toContain('1,200')
    expect(document.title).toBe('main · Scenarios · Orvano')
  })

  it('creates a table from the column builder and opens it', async () => {
    seed('owner')
    const { router } = await renderApp(`/projects/${projectId}/databases/main`)
    await expect.poll(text).toContain('tasks')
    await userEvent.click(button('New table') ?? document.body)
    await expect.poll(() => input('table-name')).not.toBeNull()
    expect(dialog()?.textContent).toContain('created_at')
    await userEvent.type(input('table-name') ?? document.body, 'notes')
    await userEvent.click(button('Add column') ?? document.body)
    await userEvent.type(input('column-0-name') ?? document.body, 'body')
    await userEvent.click(button('Create table') ?? document.body)

    await expect
      .poll(() => router.state.location.pathname)
      .toBe(`/projects/${projectId}/databases/main/tables/notes`)
    const sent = api.requests.find(
      (request) =>
        request.method === 'POST' && request.path === '/v1/console/project/databases/main/tables',
    )
    expect(sent?.body).toEqual({
      name: 'notes',
      columns: [{ name: 'body', type: 'text', required: false, unique: false }],
    })
  })

  it('checks names before sending and shows server problems under their field', async () => {
    seed('owner')
    await renderApp(`/projects/${projectId}/databases/main`)
    await expect.poll(text).toContain('tasks')
    await userEvent.click(button('New table') ?? document.body)
    await expect.poll(() => input('table-name')).not.toBeNull()
    await userEvent.type(input('table-name') ?? document.body, 'Bad Name')
    await userEvent.click(button('Add column') ?? document.body)
    await userEvent.type(input('column-0-name') ?? document.body, 'id')
    await userEvent.click(button('Create table') ?? document.body)
    await expect.poll(() => dialog()?.textContent).toContain('Use 1 to 63 characters')
    expect(dialog()?.textContent).toContain('Every table already has this column')
    expect(input('table-name')?.getAttribute('aria-invalid')).toBe('true')

    await userEvent.clear(input('table-name') ?? document.body)
    await userEvent.type(input('table-name') ?? document.body, 'tasks')
    await userEvent.clear(input('column-0-name') ?? document.body)
    await userEvent.type(input('column-0-name') ?? document.body, 'body')
    await userEvent.click(button('Create table') ?? document.body)
    await expect
      .poll(() => document.getElementById('table-name-error')?.textContent)
      .toContain("A table named 'tasks' already exists")
  })

  it('keeps New table off for viewers', async () => {
    seed('viewer')
    await renderApp(`/projects/${projectId}/databases/main`)
    await expect.poll(text).toContain('tasks')
    expect(button('New table')?.getAttribute('aria-disabled')).toBe('true')
  })
})

describe('a table page (AC-33)', () => {
  it('shows the rows in a grid with NULL for empty values and the client access banner', async () => {
    seed('viewer')
    await renderApp(`/projects/${projectId}/databases/main/tables/tasks`)
    await expect.poll(text).toContain('Write the spec')
    const headers = [...document.querySelectorAll('thead th')].map((th) => th.textContent)
    expect(headers[3]).toContain('title')
    expect(text()).toContain('NULL')
    expect(text()).toContain("Apps can't read or write rows yet")
    expect(document.title).toBe('tasks · main · Orvano')
  })
})

describe('accessibility (AC-35)', () => {
  it.each([
    ['dark', 'compact', 'databases/main'],
    ['light', 'comfortable', 'databases/main'],
    ['dark', 'compact', 'databases/main/tables/tasks'],
    ['light', 'comfortable', 'databases/main/tables/tasks'],
    ['light', 'compact', 'databases'],
  ] as const)(
    'has no axe violations in the %s theme, %s density, on %s',
    async (theme, density, page) => {
      setMode(theme, density)
      seed('owner')
      await renderApp(`/projects/${projectId}/${page}`)
      await expect.poll(text).toContain(page.endsWith('tasks') ? 'Write the spec' : 'main')
      await settle()
      expect((await axe.run(document.body)).violations.map((violation) => violation.id)).toEqual([])
    },
  )

  it.each(['light', 'dark'] as const)(
    'has no axe violations with New table open and a column added, %s theme',
    async (theme) => {
      setMode(theme, 'compact')
      seed('owner')
      await renderApp(`/projects/${projectId}/databases/main`)
      await expect.poll(text).toContain('tasks')
      await userEvent.click(button('New table') ?? document.body)
      await expect.poll(() => input('table-name')).not.toBeNull()
      await userEvent.click(button('Add column') ?? document.body)
      await settle()
      expect((await axe.run(document.body)).violations.map((violation) => violation.id)).toEqual([])
    },
  )

  it('returns focus to New table when the dialog closes', async () => {
    seed('owner')
    await renderApp(`/projects/${projectId}/databases/main`)
    await expect.poll(text).toContain('tasks')
    const open = button('New table')
    await userEvent.click(open ?? document.body)
    await expect.poll(() => input('table-name')).not.toBeNull()
    await userEvent.keyboard('{Escape}')
    await expect.poll(() => document.activeElement).toBe(open)
  })
})
