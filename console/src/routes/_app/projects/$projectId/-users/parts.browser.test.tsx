import { OrvanoError } from '@orvano/console-client'
import type { Session, SigningKey } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { CreateUserDialog, SessionsTable, SigningKeysPanel, UserStatusBadge } from './parts'

// Spec 0004 AC-22 and AC-29: the Users page parts and the signing keys panel, for every role, at
// WCAG AA.

const session: Session = {
  id: 's1',
  createdAt: '2026-09-01T10:00:00Z',
  lastRefreshedAt: '2026-09-02T10:00:00Z',
  userAgent: 'Mozilla/5.0 (Macintosh)',
  sdk: 'orvano-js/0.1.0',
  ipAddress: '203.0.113.7',
  current: false,
}

const signingKeys: SigningKey[] = [
  {
    id: 'newKeyNewKeyNewKeyNewK',
    status: 'active',
    createdAt: '2026-09-02T10:00:00Z',
    retireAfter: null,
  },
  {
    id: 'oldKeyOldKeyOldKeyOldK',
    status: 'retiring',
    createdAt: '2026-08-01T10:00:00Z',
    retireAfter: '2026-09-03T10:00:00Z',
  },
]

async function noAxeViolations(): Promise<void> {
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

describe('UserStatusBadge', () => {
  it('shows the status as a word', async () => {
    const screen = await render(<UserStatusBadge status="blocked" />)
    await expect.element(screen.getByText('Blocked')).toBeVisible()
  })
})

describe('SessionsTable', () => {
  it('lists where a session signed in from and ends one after a confirmation', async () => {
    const onEnd = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <SessionsTable
          sessions={[session]}
          loading={false}
          error={undefined}
          onRetry={() => undefined}
          hasMore={false}
          onLoadMore={() => undefined}
          endReason={undefined}
          onEnd={onEnd}
        />
      </main>,
    )

    await expect.element(screen.getByText('203.0.113.7')).toBeVisible()
    await expect.element(screen.getByText('orvano-js/0.1.0')).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'End session' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'End session' }).click()

    await expect.poll(() => onEnd.mock.calls.length).toBe(1)
    expect(onEnd).toHaveBeenCalledWith(session)
  })

  it('tells a viewer why they cannot end a session', async () => {
    const onEnd = vi.fn()
    const screen = await render(
      <SessionsTable
        sessions={[session]}
        loading={false}
        error={undefined}
        onRetry={() => undefined}
        hasMore={false}
        onLoadMore={() => undefined}
        endReason="Developers and owners only"
        onEnd={onEnd}
      />,
    )

    const button = screen.getByRole('button', { name: 'End session' })
    await expect.element(button).toHaveAccessibleDescription('Developers and owners only')
    await button.click({ force: true })
    expect(onEnd).not.toHaveBeenCalled()
  })
})

describe('CreateUserDialog', () => {
  it('checks the fields, shows a taken email in words, and closes once created', async () => {
    const onCreate = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(409, 'user_already_exists', 'raw', null))
      .mockResolvedValueOnce(undefined)
    const screen = await render(<CreateUserDialog disabledReason={undefined} onCreate={onCreate} />)

    await screen.getByRole('button', { name: 'Create user' }).click()
    const dialog = screen.getByRole('dialog')
    await dialog.getByRole('button', { name: 'Create user' }).click()
    await expect.element(dialog.getByText('Enter an email.')).toBeVisible()
    await noAxeViolations()

    await dialog.getByLabelText('Email').fill('ada@example.com')
    await dialog.getByLabelText('Password').fill('correct horse battery')
    await dialog.getByRole('button', { name: 'Create user' }).click()
    await expect
      .element(dialog.getByText('An account with this email already exists. Sign in instead.'))
      .toBeVisible()
    await dialog.getByRole('button', { name: 'Create user' }).click()

    await expect.element(screen.getByRole('dialog')).not.toBeInTheDocument()
    expect(onCreate).toHaveBeenLastCalledWith({
      email: 'ada@example.com',
      password: 'correct horse battery',
      name: '',
    })
  })
})

describe('SigningKeysPanel', () => {
  it('shows key IDs, status, and dates, and rotates after a confirmation', async () => {
    const onRotate = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <SigningKeysPanel
          keys={signingKeys}
          loading={false}
          error={undefined}
          onRetry={() => undefined}
          rotateReason={undefined}
          onRotate={onRotate}
        />
      </main>,
    )

    await expect.element(screen.getByText('oldKeyOldKeyOldKeyOldK')).toBeVisible()
    await expect.element(screen.getByText('Retiring')).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'Rotate key' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Rotate key' }).click()

    await expect.poll(() => onRotate.mock.calls.length).toBe(1)
  })

  it('tells developers and viewers that only owners rotate', async () => {
    const onRotate = vi.fn()
    const screen = await render(
      <SigningKeysPanel
        keys={signingKeys}
        loading={false}
        error={undefined}
        onRetry={() => undefined}
        rotateReason="Owners only"
        onRotate={onRotate}
      />,
    )

    await expect
      .element(screen.getByRole('button', { name: 'Rotate key' }))
      .toHaveAccessibleDescription('Owners only')
  })
})
