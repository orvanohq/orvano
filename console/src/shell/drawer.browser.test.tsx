import { LayoutDashboard } from 'lucide-react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { page, userEvent } from 'vitest/browser'

import { Sidebar, SidebarProvider, SidebarTrigger } from '@/components/ui/sidebar'
import { TooltipProvider } from '@/components/ui/tooltip'
import { useCloseDrawerOnNavigate } from '@/shell/drawer'
import type { NavEntry } from '@/shell/nav'
import { SidebarNav } from '@/shell/sidebar-nav'
import { renderInRouter } from '@/test/router'

const entries: readonly NavEntry[] = [
  {
    id: 'overview',
    label: 'Overview',
    icon: LayoutDashboard,
    to: '/projects/$projectId',
    exact: true,
  },
]

function DrawerCloser() {
  useCloseDrawerOnNavigate()
  return null
}

/** The frame's drawer parts: the menu button, the drawer with its links, and a title to land on. */
function Frame() {
  return (
    <TooltipProvider>
      <SidebarProvider>
        <DrawerCloser />
        <SidebarTrigger aria-label="Open navigation" />
        <Sidebar label="Project">
          <SidebarNav
            label="Project navigation"
            entries={entries}
            params={{ projectId: 'abc' }}
            role="owner"
          />
        </Sidebar>
        <h1 id="page-title" tabIndex={-1}>
          Title
        </h1>
      </SidebarProvider>
    </TooltipProvider>
  )
}

const paths = ['/projects/$projectId', '/orgs'] as const
const drawer = () => document.querySelector('[role=dialog]')

beforeEach(async () => {
  window.localStorage.clear()
  // Below 1024 px the sidebar is a drawer.
  await page.viewport(360, 740)
})

afterEach(async () => {
  await page.viewport(1280, 800)
})

describe('the sidebar drawer (AC-16)', () => {
  it('closes when the location changes, even when the drawer did not cause it', async () => {
    const { router } = await renderInRouter(<Frame />, { at: '/projects/abc', paths })
    await userEvent.click(page.getByRole('button', { name: 'Open navigation' }))
    await expect.poll(drawer).not.toBeNull()

    await router.navigate({ to: '/orgs' })

    await expect.poll(drawer).toBeNull()
  })

  it('closes when you pick the link for the page you are already on', async () => {
    await renderInRouter(<Frame />, { at: '/projects/abc', paths })
    await userEvent.click(page.getByRole('button', { name: 'Open navigation' }))
    await expect.poll(drawer).not.toBeNull()

    await userEvent.click(page.getByRole('link', { name: 'Overview' }))

    await expect.poll(drawer).toBeNull()
  })

  it('stays shut when the location did not change', async () => {
    await renderInRouter(<Frame />, { at: '/projects/abc', paths })
    expect(drawer()).toBeNull()
  })
})
