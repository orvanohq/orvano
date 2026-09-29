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

describe('the Platforms table (AC-17)', () => {
  it('has the Type, Name, Identifier, and Added columns, the type as a label', async () => {
    await openPlatforms('owner')
    const headers = [...document.querySelectorAll('thead th')].map((th) => th.textContent.trim())
    expect(headers.slice(0, 4)).toEqual(['Type', 'Name', 'Identifier', 'Added'])
    const android = [...document.querySelectorAll('tbody tr')].find((tr) =>
      tr.textContent.includes('Android app'),
    )
    expect(android?.querySelector('td')?.textContent).toBe('Android')
    expect(android?.querySelector('td svg')).not.toBeNull()
  })

  it('shows "No platforms yet" with Add platform on a new project', async () => {
    seed('owner')
    api.platforms = []
    await renderApp(`/projects/${projectId}/platforms`)
    await expect.poll(text).toContain('No platforms yet')
    expect(document.querySelector('[data-slot=empty-action]')?.textContent).toBe('Add platform')
  })
})

describe('adding a platform (AC-18, AC-19)', () => {
  const posts = () =>
    api.requests.filter(
      (request) => request.method === 'POST' && request.path === '/v1/console/project/platforms',
    )
  const dialogText = () => document.querySelector('[role=dialog]')?.textContent ?? ''
  const hint = () => document.getElementById('platform-identifier-hint')?.textContent
  const label = () => document.querySelector('label[for=platform-identifier]')?.textContent
  const submit = () =>
    userEvent.click(document.querySelector('[role=dialog] button[type=submit]') ?? document.body)

  async function openAdd() {
    await openPlatforms('developer')
    await userEvent.click(button('Add platform') ?? document.body)
    await expect.poll(() => input('platform-name')).not.toBeNull()
  }

  async function chooseType(name: string) {
    const option = () =>
      [...document.querySelectorAll('[role=option]')].find((item) => item.textContent === name)
    await userEvent.click(document.querySelector('#platform-type') ?? document.body)
    await expect.poll(option).toBeDefined()
    await userEvent.click(option() ?? document.body)
    await expect.poll(() => document.querySelector('#platform-type')?.textContent).toContain(name)
  }

  it('starts on Web, asks for a hostname, and carries the Flutter note', async () => {
    await openAdd()
    expect(document.querySelector('#platform-type')?.textContent).toContain('Web')
    expect(label()).toBe('Hostname')
    expect(input('platform-identifier')?.placeholder).toBe('app.example.com')
    expect(hint()).toContain('Scheme and port are ignored')
    expect(dialogText()).toContain(
      'Flutter apps: add one platform for each target you ship (Web, Android, iOS, and so on).',
    )
  })

  it.each([
    ['Android', 'Package name', 'com.example.app', 'applicationId'],
    ['iOS', 'Bundle ID', 'com.example.app', 'Xcode'],
    ['Windows', 'Label', 'My desktop app', "can't verify desktop apps"],
  ])(
    'changes the identifier label, placeholder, and hint for %s',
    async (type, fieldLabel, placeholder, hintText) => {
      await openAdd()
      await chooseType(type)
      expect(label()).toBe(fieldLabel)
      expect(input('platform-identifier')?.placeholder).toBe(placeholder)
      expect(hint()).toContain(hintText)
    },
  )

  it('reduces a pasted URL to its lowercase hostname when the field loses focus', async () => {
    await openAdd()
    await userEvent.fill(
      input('platform-identifier') ?? document.body,
      'https://App.example.com:3000/login',
    )
    await userEvent.tab()
    await expect.poll(() => input('platform-identifier')?.value).toBe('app.example.com')
  })

  it.each(['*', '*.com'])('refuses the web wildcard %s and sends nothing', async (identifier) => {
    await openAdd()
    await userEvent.fill(input('platform-name') ?? document.body, 'Everything')
    await userEvent.fill(input('platform-identifier') ?? document.body, identifier)
    await submit()
    await expect
      .poll(() => document.getElementById('platform-identifier-error')?.textContent)
      .toBeTruthy()
    expect(input('platform-identifier')?.getAttribute('aria-invalid')).toBe('true')
    expect(posts()).toEqual([])
  })

  it('refuses an Android identifier that is not a package name', async () => {
    await openAdd()
    await chooseType('Android')
    await userEvent.fill(input('platform-name') ?? document.body, 'Android app')
    await userEvent.fill(input('platform-identifier') ?? document.body, 'not a package')
    await submit()
    await expect
      .poll(() => document.getElementById('platform-identifier-error')?.textContent)
      .toBe('Use a package name like com.example.app.')
    expect(posts()).toEqual([])
  })

  it('adds an Android platform with the trimmed values and lists it', async () => {
    await openAdd()
    api.platforms = []
    await chooseType('Android')
    await userEvent.fill(input('platform-name') ?? document.body, '  Release  ')
    await userEvent.fill(input('platform-identifier') ?? document.body, 'com.example.release')
    await submit()
    await expect.poll(text).toContain('Platform added')
    expect(posts().map((request) => request.body)).toEqual([
      { type: 'android', name: 'Release', identifier: 'com.example.release' },
    ])
    await expect.poll(() => document.querySelector('[data-slot=dialog-content]')).toBeNull()
    await expect.poll(text).toContain('com.example.release')
  })

  it('shows a server refusal in the form alert and keeps the dialog open', async () => {
    await openAdd()
    api.failNext(
      'POST',
      /\/platforms$/,
      400,
      'validation_failed',
      'This project already has that platform.',
    )
    await userEvent.fill(input('platform-name') ?? document.body, 'Web again')
    await userEvent.fill(input('platform-identifier') ?? document.body, 'app.example.com')
    await submit()
    await expect
      .poll(() => document.querySelector('[role=dialog] [role=alert]')?.textContent)
      .toContain('This project already has that platform.')
    expect(dialogText()).toContain("Couldn't add the platform")
    expect(input('platform-name')?.value).toBe('Web again')
  })
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
