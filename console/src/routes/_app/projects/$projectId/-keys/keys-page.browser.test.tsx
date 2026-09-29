import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'
import { render } from 'vitest-browser-react'

import { renderApp, setMode } from '@/test/app'
import {
  accountId,
  installFakeApi,
  makeKey,
  makeOrg,
  makeProject,
  type FakeApi,
} from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

// Spec 0007, AC-12, AC-16, AC-22: the keys table and its Created by column (named since spec 0008,
// AC-25), who may delete which key, and the page's accessibility.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'
const teammate = 'user00000000000000002'

function seed(role: OrgRole) {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001' })]
  api.apiKeys = [
    makeKey({ id: 'key00000000000000001', name: 'Mine', prefix: 'orv_mine0000' }),
    makeKey({
      id: 'key00000000000000002',
      name: 'Theirs',
      prefix: 'orv_their000',
      createdByUserId: teammate,
      createdBy: { id: teammate, name: 'Grace Hopper', email: 'grace@example.com' },
    }),
    makeKey({
      id: 'key00000000000000003',
      name: 'Unnamed',
      prefix: 'orv_unnamed0',
      createdByUserId: 'user00000000000000003',
      createdBy: { id: 'user00000000000000003', name: null, email: 'linus@example.com' },
    }),
    makeKey({
      id: 'key00000000000000004',
      name: 'Orphan',
      prefix: 'orv_orphan00',
      createdByUserId: 'user00000000000000004',
      createdBy: null,
    }),
  ]
  api.requests = []
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
/** The reason a disabled button gives, from its description. */
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}

async function openKeys(role: OrgRole) {
  seed(role)
  await renderApp(`/projects/${projectId}/keys`)
  await expect.poll(text).toContain('Theirs')
}

beforeEach(() => {
  setMode('dark', 'compact')
})

afterEach(() => {
  setMode('dark', 'compact')
})

describe('Created by (spec 0008, AC-25)', () => {
  it('says "You" for your keys, else the name, else the email, and "Deleted account" when gone', async () => {
    await openKeys('owner')
    const rows = [...document.querySelectorAll('tbody tr')].map((row) => row.textContent)
    expect(rows.find((row) => row.includes('Mine'))).toContain('You')
    expect(rows.find((row) => row.includes('Theirs'))).toContain('Grace Hopper')
    expect(rows.find((row) => row.includes('Unnamed'))).toContain('linus@example.com')
    expect(rows.find((row) => row.includes('Orphan'))).toContain('Deleted account')
    expect(text()).not.toContain('Teammate')
    expect(api.account.id).toBe(accountId)
  })

  it('shows a skeleton, never a name, while your account is still loading', async () => {
    // Loaded here, not at the top: it reaches the console client, which must see the fake `fetch`.
    const { CreatedBy } = await import('./columns')
    const screen = await render(
      <table>
        <tbody>
          <tr>
            <td>
              <CreatedBy
                apiKey={{
                  createdByUserId: teammate,
                  createdBy: { id: teammate, name: 'Grace Hopper', email: 'grace@example.com' },
                }}
                accountId={undefined}
              />
            </td>
          </tr>
        </tbody>
      </table>,
    )
    expect(screen.container.textContent).toBe('Loading')
    expect(screen.container.querySelector('[data-slot=skeleton]')).not.toBeNull()
  })
})

describe('the keys table (AC-12)', () => {
  const past = '2020-01-01T00:00:00.000Z'
  const future = '2099-01-01T00:00:00.000Z'

  async function openWith(extra: Parameters<typeof makeKey>[0][]) {
    seed('owner')
    api.apiKeys = extra.map((key) => makeKey(key))
    await renderApp(`/projects/${projectId}/keys`)
    await expect.poll(() => document.querySelectorAll('tbody tr').length).toBe(extra.length)
  }
  const row = (name: string) =>
    [...document.querySelectorAll('tbody tr')].find((tr) => tr.textContent.includes(name))

  it('has the Name, Key, Scopes, Expires, Last used, Created, and Created by columns', async () => {
    await openWith([{ id: 'key00000000000000001' }])
    const headers = [...document.querySelectorAll('thead th')].map((th) => th.textContent.trim())
    expect(headers).toEqual([
      'Name',
      'Key',
      'Scopes',
      'Expires',
      'Last used',
      'Created',
      'Created by',
      'Actions',
    ])
  })

  it('shows the 12 character prefix and an ellipsis in mono, never more', async () => {
    await openWith([{ id: 'key00000000000000001', prefix: 'orv_abcdefgh' }])
    const code = row('Deploy')?.querySelector('code')
    expect(code?.textContent).toBe('orv_abcdefgh…')
    expect(code?.className).toContain('font-mono')
  })

  it('shows one badge per scope', async () => {
    await openWith([{ id: 'key00000000000000001', scopes: ['users.read', 'users.write'] }])
    const badges = [...(row('Deploy')?.querySelectorAll('[data-slot=badge]') ?? [])].map(
      (badge) => badge.textContent,
    )
    expect(badges).toEqual(['users.read', 'users.write'])
  })

  it('says "Never" for no expiry, "Expired" for a past one, and the date for a future one', async () => {
    await openWith([
      { id: 'key00000000000000001', name: 'Forever', expiresAt: null },
      { id: 'key00000000000000002', name: 'Old', expiresAt: past },
      { id: 'key00000000000000003', name: 'Later', expiresAt: future },
    ])
    const expires = (name: string) => row(name)?.querySelectorAll('td')[3]
    expect(expires('Forever')?.textContent).toBe('Never')
    expect(expires('Old')?.textContent).toBe('Expired')
    expect(expires('Later')?.querySelector('time')?.getAttribute('dateTime')).toBe(future)
  })

  it('says "Never" for a key that was never used', async () => {
    await openWith([{ id: 'key00000000000000001', lastUsedAt: null }])
    expect(row('Deploy')?.querySelectorAll('td')[4]?.textContent).toBe('Never')
  })

  it('shows "No API keys yet" with Create key on a new project', async () => {
    await openWith([])
    await expect.poll(text).toContain('No API keys yet')
    const empty = document.querySelector('[data-slot=empty-action]')
    expect(empty?.textContent).toBe('Create key')
  })
})

describe('deleting a key (AC-16)', () => {
  it('lets an owner delete any key, after a confirm that names it and starts on Cancel', async () => {
    await openKeys('owner')
    await userEvent.click(button('Delete Theirs') ?? document.body)
    await expect.poll(() => document.querySelector('[role=alertdialog]')).not.toBeNull()
    const dialog = document.querySelector('[role=alertdialog]')
    expect(dialog?.textContent).toContain('Theirs')
    expect(dialog?.textContent).toContain('orv_their000…')
    expect(dialog?.textContent).toContain("can't be undone")
    await expect.poll(() => document.activeElement?.textContent).toBe('Cancel')

    await userEvent.click(button('Delete key') ?? document.body)
    await expect.poll(text).toContain('API key deleted')
    await expect.poll(() => text().includes('orv_their000')).toBe(false)
    expect(
      api.requests.filter((request) => request.method === 'DELETE').map((r) => r.path),
    ).toEqual(['/v1/console/project/keys/key00000000000000002'])
  })

  it("lets a developer delete their own key but not a teammate's, and says why", async () => {
    await openKeys('developer')
    expect(button('Delete Mine')?.getAttribute('aria-disabled')).toBeNull()
    expect(button('Delete Theirs')?.getAttribute('aria-disabled')).toBe('true')
    expect(reason(button('Delete Theirs'))).toBe('You can delete only keys you created')
  })

  it('disables Create key and every Delete for a viewer, with the role reason', async () => {
    await openKeys('viewer')
    for (const name of ['Create key', 'Delete Mine', 'Delete Theirs']) {
      expect(button(name)?.getAttribute('aria-disabled'), name).toBe('true')
      expect(reason(button(name)), name).toBe('Developers and owners only')
    }
  })

  it('shows an error toast and the real list when the key is already gone (AC-10)', async () => {
    await openKeys('owner')
    api.failNext('DELETE', /\/keys\//, 404, 'not_found', 'No such key.')
    api.apiKeys = api.apiKeys.filter((key) => key.name !== 'Theirs')
    await userEvent.click(button('Delete Theirs') ?? document.body)
    await userEvent.click(button('Delete key') ?? document.body)
    await expect.poll(text).toContain("Couldn't delete the key")
    await expect.poll(() => text().includes('orv_their000')).toBe(false)
    expect(document.querySelector('[role=alertdialog]')).toBeNull()
  })

  it('keeps the dialog open with the server message when the API refuses', async () => {
    await openKeys('owner')
    api.failNext('DELETE', /\/keys\//, 403, 'forbidden', 'You can delete only keys you created.')
    await userEvent.click(button('Delete Theirs') ?? document.body)
    await userEvent.click(button('Delete key') ?? document.body)
    await expect
      .poll(() => document.querySelector('[role=alertdialog] [role=alert]')?.textContent)
      .toContain('You can delete only keys you created.')
  })
})

describe('the API keys page and accessibility (AC-22)', () => {
  it('titles the page', async () => {
    await openKeys('owner')
    await expect.poll(() => document.title).toBe('API keys · Scenarios · Orvano')
    expect(document.querySelector('h1#page-title')?.textContent).toBe('API keys')
  })

  it.each([
    ['dark', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'compact'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in the %s theme, %s density', async (theme, density) => {
    setMode(theme, density)
    await openKeys('developer')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })

  it('has no axe violations with the delete confirm open', async () => {
    await openKeys('owner')
    await userEvent.click(button('Delete Mine') ?? document.body)
    await expect.poll(() => document.querySelector('[role=alertdialog]')).not.toBeNull()
    await new Promise((resolve) => setTimeout(resolve, 300))
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
