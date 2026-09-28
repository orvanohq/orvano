import axe from 'axe-core'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

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

// Spec 0007, AC-12, AC-16, AC-22: the keys table's Created by column, who may delete which key,
// and the page's accessibility.

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

describe('Created by (AC-12)', () => {
  it('says "You" for your keys and "Teammate" for the others', async () => {
    await openKeys('owner')
    const rows = [...document.querySelectorAll('tbody tr')].map((row) => row.textContent)
    expect(rows.find((row) => row.includes('Mine'))).toContain('You')
    expect(rows.find((row) => row.includes('Theirs'))).toContain('Teammate')
    expect(api.account.id).toBe(accountId)
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
