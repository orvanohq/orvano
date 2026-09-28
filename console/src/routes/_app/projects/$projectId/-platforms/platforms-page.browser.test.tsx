import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makePlatform, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole } from '@orvano/console-client'

// Spec 0007, AC-20 and AC-22: editing (changed fields only) and deleting platforms, role gating,
// and the page's accessibility.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'

function seed(role: OrgRole) {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001' })]
  api.platforms = [
    makePlatform({ id: 'plat0000000000000001', name: 'Web app', identifier: 'app.example.com' }),
    makePlatform({
      id: 'plat0000000000000002',
      type: 'android',
      name: 'Android app',
      identifier: 'com.example.app',
    }),
  ]
  api.requests = []
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.getAttribute('aria-label') === name || element.textContent === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const input = (id: string) => document.querySelector<HTMLInputElement>(`#${id}`)
const settle = () => new Promise((resolve) => setTimeout(resolve, 300))

async function openPlatforms(role: OrgRole) {
  seed(role)
  await renderApp(`/projects/${projectId}/platforms`)
  await expect.poll(text).toContain('com.example.app')
}

afterEach(() => {
  setMode('dark', 'compact')
})

describe('editing a platform (AC-20)', () => {
  it('opens the form filled in, with the type as read only text', async () => {
    await openPlatforms('developer')
    await userEvent.click(button('Edit Android app') ?? document.body)
    await expect.poll(() => input('platform-name')?.value).toBe('Android app')
    expect(input('platform-identifier')?.value).toBe('com.example.app')
    expect(document.querySelector('#platform-type')).toBeNull()
    expect(document.querySelector('[role=dialog]')?.textContent).toContain('Android')
    expect(document.querySelector('[role=dialog]')?.textContent).toContain('Package name')
  })

  it('sends only the fields you changed', async () => {
    await openPlatforms('owner')
    await userEvent.click(button('Edit Android app') ?? document.body)
    await expect.poll(() => input('platform-name')?.value).toBe('Android app')
    await userEvent.fill(input('platform-name') ?? document.body, 'Android release')
    await userEvent.click(button('Save changes') ?? document.body)
    await expect.poll(text).toContain('Platform saved')
    const patches = api.requests.filter((request) => request.method === 'PATCH')
    expect(patches.map((request) => [request.path, request.body])).toEqual([
      ['/v1/console/project/platforms/plat0000000000000002', { name: 'Android release' }],
    ])
    await expect.poll(text).toContain('Android release')
  })

  it('sends nothing when nothing changed', async () => {
    await openPlatforms('owner')
    await userEvent.click(button('Edit Web app') ?? document.body)
    await expect.poll(() => input('platform-name')?.value).toBe('Web app')
    await userEvent.click(button('Save changes') ?? document.body)
    await expect.poll(() => document.querySelector('[role=dialog]')).toBeNull()
    expect(api.requests.filter((request) => request.method === 'PATCH')).toEqual([])
  })
})

describe('deleting a platform (AC-20)', () => {
  it('confirms with the platform named, starting on Cancel, then removes it', async () => {
    await openPlatforms('developer')
    await userEvent.click(button('Delete Web app') ?? document.body)
    await expect.poll(() => document.querySelector('[role=alertdialog]')).not.toBeNull()
    const dialog = document.querySelector('[role=alertdialog]')?.textContent
    expect(dialog).toContain('Web app')
    expect(dialog).toContain('lose access')
    await expect.poll(() => document.activeElement?.textContent).toBe('Cancel')
    await userEvent.click(button('Delete platform') ?? document.body)
    await expect.poll(text).toContain('Platform deleted')
    await expect.poll(() => text().includes('app.example.com')).toBe(false)
  })
})

describe('platforms and roles (AC-18, AC-20)', () => {
  it('disables Add, Edit, and Delete for a viewer, with the role reason', async () => {
    await openPlatforms('viewer')
    for (const name of ['Add platform', 'Edit Web app', 'Delete Web app']) {
      expect(button(name)?.getAttribute('aria-disabled'), name).toBe('true')
      expect(reason(button(name)), name).toBe('Developers and owners only')
    }
  })
})

describe('the Platforms page and accessibility (AC-22)', () => {
  it('titles the page', async () => {
    await openPlatforms('owner')
    await expect.poll(() => document.title).toBe('Platforms · Scenarios · Orvano')
  })

  it.each([
    ['dark', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'compact'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in the %s theme, %s density', async (theme, density) => {
    setMode(theme, density)
    await openPlatforms('owner')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })

  it.each(['light', 'dark'] as const)(
    'has no axe violations with the edit dialog open, %s theme',
    async (theme) => {
      setMode(theme, 'compact')
      await openPlatforms('owner')
      await userEvent.click(button('Edit Android app') ?? document.body)
      await expect.poll(() => input('platform-name')?.value).toBe('Android app')
      await settle()
      const results = await axe.run(document.body)
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )

  it('returns focus to Edit when the dialog closes', async () => {
    await openPlatforms('owner')
    const edit = button('Edit Web app')
    await userEvent.click(edit ?? document.body)
    await expect.poll(() => input('platform-name')?.value).toBe('Web app')
    await userEvent.keyboard('{Escape}')
    await expect.poll(() => document.activeElement).toBe(edit)
  })
})
