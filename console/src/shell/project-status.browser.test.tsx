import axe from 'axe-core'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { formatFull } from '@/lib/format'
import { ProjectStatusPanel } from '@/shell/project-status'
import type { Project, ProjectStatus } from '@orvano/console-client'

const project: Project = {
  id: 'scenarios0000000000a',
  orgId: 'org0000000000000000a',
  name: 'Scenarios',
  status: 'active',
  deletedAt: null,
  purgeAfter: null,
  purgeFailedAt: null,
  createdAt: '2026-06-01T10:00:00.000Z',
  updatedAt: '2026-06-01T10:00:00.000Z',
}

function panel(status: ProjectStatus, overrides: Partial<Project> = {}) {
  return (
    <main>
      <ProjectStatusPanel project={{ ...project, status, ...overrides }} />
    </main>
  )
}

const text = () => document.body.textContent
const heading = () => document.querySelector('h1')

afterEach(() => {
  vi.useRealTimers()
})

describe('project status panels (AC-18)', () => {
  it('shows nothing for an active project, so its own pages show instead', async () => {
    await render(panel('active'))
    expect(heading()).toBeNull()
  })

  it('shows "Setting up" for a project that is provisioning, in a live region', async () => {
    await render(panel('provisioning'))
    expect(heading()?.textContent).toBe('Setting up')
    expect(document.querySelector('[role=status]')?.textContent).toContain(
      'This page updates when it is ready.',
    )
  })

  it('does not warn about a slow setup at first', async () => {
    await render(panel('provisioning'))
    expect(text()).not.toContain('taking longer than usual')
  })

  it('adds the "taking longer than usual" note after 60 seconds, and not before', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    await render(panel('provisioning'))

    await vi.advanceTimersByTimeAsync(59_999)
    expect(text()).not.toContain('taking longer than usual')

    await vi.advanceTimersByTimeAsync(1)
    await expect.poll(text).toContain('This is taking longer than usual.')
  })

  it('shows "Setup failed" for a failed project, without a live region', async () => {
    await render(panel('failed'))
    expect(heading()?.textContent).toBe('Setup failed')
    expect(text()).toContain('Something went wrong while setting this project up.')
    expect(document.querySelector('[role=status]')).toBeNull()
  })

  it('shows "Being deleted" with the purge date for a project being deleted', async () => {
    const purgeAfter = '2026-07-15T12:00:00.000Z'
    await render(panel('deleting', { purgeAfter }))
    expect(heading()?.textContent).toBe('Being deleted')
    expect(text()).toContain(`is purged for good on ${formatFull(purgeAfter)}.`)
  })

  it('says only that it is being deleted when there is no purge date', async () => {
    await render(panel('deleting'))
    expect(text()).toContain('This project is being deleted.')
    expect(text()).not.toContain('purged for good')
  })

  it('notes a failed purge when the API reports one', async () => {
    const purgeFailedAt = '2026-07-16T08:30:00.000Z'
    await render(panel('deleting', { purgeAfter: '2026-07-15T12:00:00.000Z', purgeFailedAt }))
    expect(text()).toContain(`The last purge attempt failed on ${formatFull(purgeFailedAt)}.`)
  })

  it('does not mention a purge failure when there was none', async () => {
    await render(panel('deleting', { purgeAfter: '2026-07-15T12:00:00.000Z' }))
    expect(text()).not.toContain('purge attempt failed')
  })

  it.each(['provisioning', 'failed', 'deleting'] as const)(
    'names the project and leaves an action slot in the %s panel',
    async (status) => {
      await render(panel(status))
      expect(text()).toContain('Scenarios')
      expect(document.querySelector('[data-slot=status-action]')).not.toBeNull()
    },
  )
})

describe('project status panels and page conventions (AC-23)', () => {
  it.each([
    ['provisioning', 'Setting up'],
    ['failed', 'Setup failed'],
    ['deleting', 'Being deleted'],
  ] as const)('renders the %s title as the one page heading', async (status, title) => {
    await render(panel(status))
    expect(document.querySelectorAll('h1#page-title')).toHaveLength(1)
    expect(document.title).toBe(`${title} · Scenarios · Orvano`)
  })
})

describe('project status panels and accessibility', () => {
  it.each(['provisioning', 'failed', 'deleting'] as const)(
    'has no axe violations in the %s panel',
    async (status) => {
      await render(
        panel(status, {
          purgeAfter: '2026-07-15T12:00:00.000Z',
          purgeFailedAt: '2026-07-16T08:30:00.000Z',
        }),
      )
      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
      expect(results.violations.map((violation) => violation.id)).toEqual([])
    },
  )
})
