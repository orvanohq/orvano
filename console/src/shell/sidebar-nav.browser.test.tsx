import { Database, LayoutDashboard, ShieldAlert } from 'lucide-react'
import { beforeEach, describe, expect, it } from 'vitest'

import { SidebarProvider } from '@/components/ui/sidebar'
import { TooltipProvider } from '@/components/ui/tooltip'
import type { NavEntry } from '@/shell/nav'
import { SidebarNav } from '@/shell/sidebar-nav'
import { renderInRouter } from '@/test/router'
import type { OrgRole } from '@orvano/console-client'

// Entries name routes that only later rows add, so the `to` values are cast to the router's type.
const entries: readonly NavEntry[] = [
  {
    id: 'overview',
    label: 'Overview',
    icon: LayoutDashboard,
    to: '/projects/$projectId',
    exact: true,
  },
  {
    id: 'database',
    label: 'Database',
    icon: Database,
    to: '/projects/$projectId/database' as NavEntry['to'],
  },
  {
    id: 'danger',
    label: 'Danger zone',
    icon: ShieldAlert,
    to: '/projects/$projectId/danger' as NavEntry['to'],
    minRole: 'owner',
  },
]

const paths = [
  '/projects/$projectId',
  '/projects/$projectId/database',
  '/projects/$projectId/danger',
] as const

/** `role` is 'unknown' while the org has not loaded yet (the layout then passes `undefined`). */
async function renderNav(at: string, role: OrgRole | 'unknown' = 'owner') {
  return await renderInRouter(
    <TooltipProvider>
      <SidebarProvider>
        <SidebarNav
          label="Project navigation"
          entries={entries}
          params={{ projectId: 'abc' }}
          role={role === 'unknown' ? undefined : role}
        />
      </SidebarProvider>
    </TooltipProvider>,
    { at, paths },
  )
}

const links = () => [...document.querySelectorAll<HTMLAnchorElement>('nav a')]
const current = () =>
  links()
    .filter((link) => link.getAttribute('aria-current') === 'page')
    .map((link) => link.textContent)

beforeEach(() => {
  window.localStorage.clear()
})

describe('SidebarNav (AC-15)', () => {
  it('names its navigation landmark', async () => {
    await renderNav('/projects/abc')
    expect(document.querySelector('nav')?.getAttribute('aria-label')).toBe('Project navigation')
  })

  it('marks only the current entry with aria-current="page"', async () => {
    await renderNav('/projects/abc/database')
    expect(current()).toEqual(['Database'])
  })

  it('keeps Overview current only on its own path, not on the pages below it', async () => {
    await renderNav('/projects/abc')
    expect(current()).toEqual(['Overview'])
    document.body.innerHTML = ''
    await renderNav('/projects/abc/database')
    expect(current()).not.toContain('Overview')
  })

  it('links every entry into the project in context', async () => {
    await renderNav('/projects/abc')
    expect(links().map((link) => link.getAttribute('href'))).toEqual([
      '/projects/abc',
      '/projects/abc/database',
      '/projects/abc/danger',
    ])
  })
})

describe('SidebarNav and roles (AC-22)', () => {
  it.each(['viewer', 'developer'] as const)('hides an owner only entry from a %s', async (role) => {
    await renderNav('/projects/abc', role)
    expect(links().map((link) => link.textContent)).toEqual(['Overview', 'Database'])
  })

  it('shows an owner only entry to an owner', async () => {
    await renderNav('/projects/abc', 'owner')
    expect(links().map((link) => link.textContent)).toContain('Danger zone')
  })

  it('hides an owner only entry until the role is known', async () => {
    await renderNav('/projects/abc', 'unknown')
    expect(links().map((link) => link.textContent)).not.toContain('Danger zone')
  })
})
