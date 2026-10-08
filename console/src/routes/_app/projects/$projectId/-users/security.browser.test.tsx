import type { Passkey, User } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { settleStyles } from '@/test/settle'

import { UserSecurity } from './security'

// Spec 0013 AC-44: the user detail's Security section, for writers and viewers, at WCAG AA.

const user: User = {
  id: 'user-1',
  email: 'ada@example.com',
  emailVerified: true,
  emailVerifiedAt: '2026-09-01T10:00:00Z',
  name: 'Ada',
  status: 'active',
  metadata: {},
  createdAt: '2026-09-01T10:00:00Z',
  lastSignInAt: null,
  providers: [],
  hasPassword: true,
  mfaEnabled: true,
}

const passkeys: Passkey[] = [
  {
    id: 'pk1',
    name: 'iPhone',
    createdAt: '2026-09-01T10:00:00Z',
    lastUsedAt: '2026-09-02T10:00:00Z',
    synced: true,
    active: true,
  },
  {
    id: 'pk2',
    name: 'YubiKey',
    createdAt: '2026-09-01T11:00:00Z',
    lastUsedAt: null,
    synced: false,
    active: false,
  },
]

async function noAxeViolations(): Promise<void> {
  await settleStyles()
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

async function renderSecurity(overrides: Partial<Parameters<typeof UserSecurity>[0]> = {}) {
  const onReset = vi.fn().mockResolvedValue(undefined)
  const onRemove = vi.fn().mockResolvedValue(undefined)
  const screen = await render(
    <main>
      <UserSecurity
        user={user}
        passkeys={passkeys}
        loading={false}
        error={undefined}
        onRetry={() => undefined}
        actionReason={undefined}
        onReset={onReset}
        onRemove={onRemove}
        {...overrides}
      />
    </main>,
  )
  return { screen, onReset, onRemove }
}

describe('UserSecurity', () => {
  it('shows MFA as a word and each passkey with its kind and state', async () => {
    const { screen } = await renderSecurity()

    await expect.element(screen.getByRole('heading', { name: 'Security' })).toBeVisible()
    await expect.element(screen.getByText('On', { exact: true })).toBeVisible()
    await expect.element(screen.getByRole('cell', { name: 'Synced' })).toBeVisible()
    await expect.element(screen.getByRole('cell', { name: 'Device bound' })).toBeVisible()
    await expect.element(screen.getByText('Inactive')).toBeVisible()
    await expect.element(screen.getByRole('cell', { name: 'Never' })).toBeVisible()
    await noAxeViolations()
  })

  it('resets MFA only after a confirmation that says every session ends', async () => {
    const { screen, onReset } = await renderSecurity()

    await screen.getByRole('button', { name: 'Reset MFA' }).click()
    const dialog = screen.getByRole('alertdialog')
    await expect.element(dialog.getByText(/every session of theirs ends/)).toBeVisible()
    await noAxeViolations()
    await dialog.getByRole('button', { name: 'Reset MFA' }).click()

    await expect.poll(() => onReset.mock.calls.length).toBe(1)
  })

  it('removes one passkey after a confirmation', async () => {
    const { screen, onRemove } = await renderSecurity()

    await screen.getByRole('button', { name: 'Remove passkey YubiKey' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Remove passkey' }).click()

    await expect.poll(() => onRemove.mock.calls.length).toBe(1)
    expect(onRemove).toHaveBeenCalledWith(passkeys[1])
  })

  it('shows a viewer the buttons with the reason they are off', async () => {
    const { screen, onReset } = await renderSecurity({ actionReason: 'Needs the developer role' })

    const reset = screen.getByRole('button', { name: 'Reset MFA' })
    await expect.element(reset).toHaveAttribute('aria-disabled', 'true')
    await reset.click({ force: true })
    expect(onReset).not.toHaveBeenCalled()
    await noAxeViolations()
  })

  it('says when there is no passkey', async () => {
    const { screen } = await renderSecurity({ passkeys: [], user: { ...user, mfaEnabled: false } })

    await expect.element(screen.getByText('No passkey yet.')).toBeVisible()
    await expect.element(screen.getByText('Off', { exact: true })).toBeVisible()
  })
})
