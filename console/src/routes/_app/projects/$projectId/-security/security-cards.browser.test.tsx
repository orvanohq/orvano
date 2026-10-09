import { OrvanoError, type AuthPolicies, type AuthPolicyValues } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { settleStyles } from '@/test/settle'

import { AppServersCard } from './app-servers-card'
import { PasswordsCard } from './passwords-card'
import { RateLimitsCard } from './rate-limits-card'

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

describe('AppServersCard', () => {
  it('saves one range per line and names a refused line by its number', async () => {
    const onSave = vi
      .fn()
      .mockRejectedValueOnce(
        new OrvanoError(
          400,
          'invalid_request',
          'trustedServerCidrs has bad entries at positions 1: each must be an address range such as 203.0.113.0/24.',
          'r',
        ),
      )
      .mockResolvedValue(undefined)
    const screen = await render(
      <AppServersCard policies={policiesOf()} readOnlyReason={undefined} onSave={onSave} />,
    )
    const field = screen.getByLabelText('Server addresses')
    await field.fill('203.0.113.10\n\n8.0.0.0/8')
    await screen.getByRole('button', { name: 'Save' }).click()
    expect(onSave).toHaveBeenCalledWith({ trustedServerCidrs: ['203.0.113.10', '8.0.0.0/8'] })
    // The second entry sits on line 3 of the textarea.
    await expect.element(screen.getByText(/^Line 3: Each must be an address range/)).toBeVisible()
    await expect.element(field).toHaveAttribute('aria-invalid', 'true')
    await noAxeViolations()

    await field.fill('203.0.113.10')
    await screen.getByRole('button', { name: 'Save' }).click()
    await vi.waitFor(() => {
      expect(onSave).toHaveBeenLastCalledWith({ trustedServerCidrs: ['203.0.113.10'] })
    })
  })
})

describe('RateLimitsCard', () => {
  it('resets a limit to its default and saves all five', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <RateLimitsCard
        policies={policiesOf({ signUpPerIp: 5 })}
        readOnlyReason={undefined}
        onSave={onSave}
      />,
    )
    await noAxeViolations()
    await expect.element(screen.getByLabelText('Sign ups per address')).toHaveValue('5')
    await screen.getByRole('button', { name: 'Reset sign ups per address to 60' }).click()
    await expect.element(screen.getByLabelText('Sign ups per address')).toHaveValue('60')
    await screen.getByLabelText('Failed sign ins per email and address').fill('5')
    await screen.getByRole('button', { name: 'Save' }).click()
    await vi.waitFor(() => {
      expect(onSave).toHaveBeenCalledWith({
        signInFailedPerEmailIp: { limit: 5, windowMinutes: 15 },
        signInFailedPerIp: 100,
        signUpPerIp: 60,
        anonymousPerIp: 30,
        emailSendPerIp: 300,
      })
    })
  })

  it('refuses a value out of bounds on its field before sending', async () => {
    const onSave = vi.fn()
    const screen = await render(
      <RateLimitsCard policies={policiesOf()} readOnlyReason={undefined} onSave={onSave} />,
    )
    await screen.getByLabelText('Failed sign ins per address').fill('5')
    await screen.getByRole('button', { name: 'Save' }).click()
    await expect.element(screen.getByText('Enter a whole number from 10 to 10000.')).toBeVisible()
    expect(onSave).not.toHaveBeenCalled()
    await noAxeViolations()
  })
})
