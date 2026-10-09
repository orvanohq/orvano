import { OrvanoError, type AuthPolicies, type AuthPolicyValues } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { settleStyles } from '@/test/settle'

import { PasswordsCard } from './passwords-card'

// Spec 0014 AC-34: the Security page's cards save only their own fields, place a refused value on
// its field, show every value to viewers with the controls disabled, and meet WCAG AA.

const defaults: AuthPolicyValues = {
  signUpsEnabled: true,
  requireVerifiedEmail: false,
  blockDisposableEmails: false,
  blockedEmailDomains: [],
  allowedEmailDomains: [],
  passwordMinLength: 8,
  passwordCommonCheck: true,
  passwordBreachedCheck: false,
  accessTokenSeconds: 900,
  sessionIdleSeconds: 2592000,
  sessionAbsoluteSeconds: 31536000,
  maxSessionsPerUser: null,
  trustedServerCidrs: [],
  signInFailedPerEmailIp: { limit: 10, windowMinutes: 15 },
  signInFailedPerIp: 100,
  signUpPerIp: 60,
  anonymousPerIp: 30,
  emailSendPerIp: 300,
}

/** The rules as the server answers them, at their defaults unless overridden. */
export function policiesOf(overrides: Partial<AuthPolicyValues> = {}): AuthPolicies {
  return {
    ...defaults,
    ...overrides,
    updatedAt: null,
    smtpAvailable: true,
    defaults,
  }
}

async function noAxeViolations(): Promise<void> {
  await settleStyles()
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

describe('PasswordsCard', () => {
  it('saves its three fields and nothing else', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <PasswordsCard policies={policiesOf()} readOnlyReason={undefined} onSave={onSave} />,
    )
    await expect.element(screen.getByRole('heading', { name: 'Passwords' })).toBeVisible()
    await noAxeViolations()

    await screen.getByLabelText('Minimum length').fill('12')
    await screen.getByRole('switch', { name: 'Refuse breached passwords' }).click()
    await screen.getByRole('button', { name: 'Save' }).click()
    await vi.waitFor(() => {
      expect(onSave).toHaveBeenCalledWith({
        passwordMinLength: 12,
        passwordCommonCheck: true,
        passwordBreachedCheck: true,
      })
    })
  })

  it('refuses a length out of bounds before sending, and shows a server refusal on its field', async () => {
    const onSave = vi
      .fn()
      .mockRejectedValue(
        new OrvanoError(400, 'invalid_request', 'passwordMinLength must be from 8 to 64.', 'r'),
      )
    const screen = await render(
      <PasswordsCard policies={policiesOf()} readOnlyReason={undefined} onSave={onSave} />,
    )
    const length = screen.getByLabelText('Minimum length')
    await length.fill('65')
    await screen.getByRole('button', { name: 'Save' }).click()
    await expect.element(screen.getByText('Enter a whole number from 8 to 64.')).toBeVisible()
    await expect.element(length).toHaveAttribute('aria-invalid', 'true')
    expect(onSave).not.toHaveBeenCalled()
    await noAxeViolations()

    await length.fill('20')
    await screen.getByRole('button', { name: 'Save' }).click()
    await expect.element(screen.getByText('passwordMinLength must be from 8 to 64.')).toBeVisible()
  })

  it('shows a viewer every value with the controls disabled', async () => {
    const onSave = vi.fn()
    const screen = await render(
      <PasswordsCard
        policies={policiesOf({ passwordMinLength: 14 })}
        readOnlyReason="Viewers can look but not change."
        onSave={onSave}
      />,
    )
    await expect.element(screen.getByLabelText('Minimum length')).toHaveValue('14')
    await expect.element(screen.getByLabelText('Minimum length')).toBeDisabled()
    await expect
      .element(screen.getByRole('switch', { name: 'Refuse common passwords' }))
      .toHaveAttribute('aria-disabled', 'true')
    await screen.getByRole('button', { name: 'Save' }).click({ force: true })
    expect(onSave).not.toHaveBeenCalled()
    await noAxeViolations()
  })
})
