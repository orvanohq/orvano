import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { EmailLogEntry } from '@orvano/console-client'

// Spec 0009, AC-20: the Log tab, for every project member.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'

const sent: EmailLogEntry = {
  id: 'email000000000000001',
  template: 'recovery',
  recipient: 'g***@example.com',
  status: 'sent',
  smtpSource: 'project',
  attempts: 1,
  errorCode: null,
  createdAt: '2026-06-01T10:00:00.000Z',
  completedAt: '2026-06-01T10:00:02.000Z',
}

function seed(emails: EmailLogEntry[]) {
  // A viewer: the log is read only, so every member sees all of it.
  api.orgs = [makeOrg({ id: 'org00000000000000001', role: 'viewer' })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001', name: 'Shop' })]
  api.emails = emails
  api.requests = []
}

const text = () => document.body.textContent

afterEach(() => {
  setMode('dark', 'compact')
})

describe('the Email Log tab', () => {
  it('lists emails with the masked recipient, status, attempts, and reason', async () => {
    seed([
      {
        ...sent,
        id: 'email000000000000002',
        status: 'queued',
        smtpSource: null,
        completedAt: null,
      },
      sent,
      {
        ...sent,
        id: 'email000000000000003',
        template: 'email_code',
        status: 'failed',
        smtpSource: null,
        attempts: 6,
        errorCode: 'email_expired',
      },
    ])
    await renderApp(`/projects/${projectId}/email/log`)
    await expect.poll(text).toContain('g***@example.com')
    await expect.poll(() => document.title).toBe('Email log · Shop · Orvano')

    const headers = [...document.querySelectorAll('thead th')].map((th) => th.textContent.trim())
    expect(headers).toEqual([
      'Template',
      'To',
      'Status',
      'Attempts',
      'Reason',
      'Created',
      'Completed',
    ])
    const rows = [...document.querySelectorAll('tbody tr')].map((tr) => tr.textContent)
    expect(rows[0]).toContain('Password reset')
    expect(rows[0]).toContain('Queued')
    expect(rows[1]).toContain('Sent')
    expect(rows[2]).toContain('Email code')
    expect(rows[2]).toContain('Failed')
    expect(rows[2]).toContain('Not sent: it waited more than 30 minutes in the queue.')
    // Created and Completed carry the exact instant for the viewer's own time zone.
    expect(document.querySelector('tbody time')?.getAttribute('datetime')).toBe(sent.createdAt)
    expect(
      document.querySelector('nav[aria-label="Email sections"] [aria-current=page]')?.textContent,
    ).toBe('Log')
  })

  it('says so when no email was sent in the last 30 days', async () => {
    seed([])
    await renderApp(`/projects/${projectId}/email/log`)
    await expect.poll(text).toContain('No emails in the last 30 days')
  })

  it.each([
    ['dark', 'compact'],
    ['light', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in %s, %s', async (theme, density) => {
    setMode(theme, density)
    seed([sent])
    await renderApp(`/projects/${projectId}/email/log`)
    await expect.poll(text).toContain('g***@example.com')
    const results = await axe.run(document.body)
    expect(results.violations.map((violation) => violation.id)).toEqual([])
  })
})
