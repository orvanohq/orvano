import axe from 'axe-core'
import { beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, type FakeApi } from '@/test/fake-api'

// Spec 0006 AC-23: while the install waits for its first admin, /sign-in points you to the setup link
// the installer printed, in place of the form. Spec 0013 AC-41: an account with MFA passes the Two
// step verification step, and the email field offers passkeys through autofill.

const api: FakeApi = installFakeApi()

const text = () => document.body.textContent
const field = (id: string) => document.getElementById(id) ?? document.body
const submitOf = (fieldId: string) =>
  document
    .getElementById(fieldId)
    ?.closest('form')
    ?.querySelector<HTMLButtonElement>('button[type=submit]') ?? document.body
const sent = (method: string, path: string) =>
  api.requests.filter((request) => request.method === method && request.path === path)

beforeEach(() => {
  api.requests = []
  api.consoleMfa = null
  api.setupRequired = false
})

async function signInWithPassword() {
  const app = await renderApp('/sign-in')
  await expect.element(app.screen.getByLabelText('Password')).toBeVisible()
  await userEvent.fill(field('sign-in-email'), 'ada@example.com')
  await userEvent.fill(field('sign-in-password'), 'correct horse battery')
  await userEvent.click(submitOf('sign-in-password'))
  return app
}

describe('/sign-in', () => {
  it('shows the finish setup notice and no form while setup is required', async () => {
    api.setupRequired = true

    const { screen } = await renderApp('/sign-in')

    await expect.element(screen.getByText('Finish setting up Orvano')).toBeVisible()
    await expect
      .element(screen.getByText('Open the setup link the installer printed on your server.'))
      .toBeVisible()
    expect(document.querySelector('input[type=password]')).toBeNull()
    expect(document.querySelector('form')).toBeNull()
  })

  it('shows the sign in form once the install has its admin', async () => {
    api.setupRequired = false

    const { screen } = await renderApp('/sign-in')

    await expect.element(screen.getByLabelText('Password')).toBeVisible()
    expect(document.body.textContent).not.toContain('Finish setting up Orvano')
  })

  it('links to /sign-up only when sign up is open (spec 0008, AC-23)', async () => {
    api.setupRequired = false
    api.consoleSignup = 'invite'
    const { screen } = await renderApp('/sign-in')
    await expect.element(screen.getByLabelText('Password')).toBeVisible()
    expect(document.querySelector('a[href="/sign-up"]')).toBeNull()

    api.consoleSignup = 'open'
    await renderApp('/sign-in')
    await expect
      .poll(() => document.querySelector('a[href="/sign-up"]')?.textContent)
      .toBe('Create account')
  })

  it('offers passkeys in the email field through autofill', async () => {
    const { screen } = await renderApp('/sign-in')

    await expect
      .element(screen.getByLabelText('Email'))
      .toHaveAttribute('autocomplete', 'username webauthn')
  })

  describe('passkey autofill', () => {
    const original = Object.getOwnPropertyDescriptor(
      PublicKeyCredential,
      'isConditionalMediationAvailable',
    )
    beforeEach(() => {
      Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', {
        configurable: true,
        value: () => Promise.resolve(true),
      })
      return () => {
        if (original !== undefined) {
          Object.defineProperty(PublicKeyCredential, 'isConditionalMediationAvailable', original)
        }
      }
    })

    it('shows a refusal, such as too many attempts, like the button does', async () => {
      api.failNext('POST', /session\/passkey-challenge$/, 429, 'rate_limited', 'Slow down.')
      const { screen } = await renderApp('/sign-in')

      await expect.element(screen.getByText("Couldn't sign in with a passkey")).toBeVisible()
      expect(text()).toContain('Too many attempts.')
    })

    it('ends quietly on a server without console passkeys', async () => {
      api.failNext('POST', /session\/passkey-challenge$/, 409, 'factor_not_enabled', 'No.')
      const { screen } = await renderApp('/sign-in')

      await expect
        .poll(() => sent('POST', '/v1/console/account/session/passkey-challenge').length)
        .toBe(1)
      await expect.element(screen.getByLabelText('Password')).toBeVisible()
      expect(text()).not.toContain("Couldn't sign in with a passkey")
    })
  })

  it('signs in at once for an account without MFA', async () => {
    const { router } = await signInWithPassword()

    await expect.poll(() => router.state.location.pathname).not.toBe('/sign-in')
    expect(sent('POST', '/v1/console/account/session/mfa')).toEqual([])
  })

  it('asks for a code for an account with MFA, then signs in', async () => {
    api.consoleMfa = ['totp', 'recovery_code']
    const { screen, router } = await signInWithPassword()

    await expect
      .element(screen.getByRole('heading', { name: 'Two step verification' }))
      .toBeVisible()
    const code = screen.getByLabelText('Authentication code')
    await expect.element(code).toHaveAttribute('autocomplete', 'one-time-code')
    await expect.element(code).toHaveAttribute('inputmode', 'numeric')
    expect(text()).not.toContain('Use a passkey')
    await userEvent.fill(field('mfa-code'), '12345')
    await userEvent.click(submitOf('mfa-code'))
    await expect.element(screen.getByText('Enter the 6 digit code.')).toBeVisible()
    expect(sent('POST', '/v1/console/account/session/mfa')).toEqual([])

    await userEvent.fill(field('mfa-code'), '123456')
    await userEvent.click(submitOf('mfa-code'))

    await expect.poll(() => router.state.location.pathname).not.toBe('/sign-in')
    expect(sent('POST', '/v1/console/account/session/mfa')[0]?.body).toEqual({ totpCode: '123456' })
  })

  it('takes a recovery code, says when a code is wrong, and starts over', async () => {
    api.consoleMfa = ['totp', 'recovery_code', 'passkey']
    const { screen } = await signInWithPassword()
    await expect.element(screen.getByRole('button', { name: 'Use a passkey' })).toBeVisible()

    await screen.getByRole('button', { name: 'Use a recovery code' }).click()
    api.failNext('POST', /session\/mfa$/, 401, 'invalid_mfa_code', 'Wrong.')
    await userEvent.fill(field('mfa-recovery-code'), 'abcde-23456')
    await userEvent.click(submitOf('mfa-recovery-code'))
    await expect.element(screen.getByText(/That recovery code didn't work/)).toBeVisible()
    expect(sent('POST', '/v1/console/account/session/mfa')[0]?.body).toEqual({
      recoveryCode: 'abcde-23456',
    })

    await screen.getByRole('button', { name: 'Start over' }).click()
    await expect.element(screen.getByLabelText('Password')).toBeVisible()
  })

  it('returns to the form with a reason when the second step expired', async () => {
    api.consoleMfa = ['totp']
    const { screen } = await signInWithPassword()
    await expect.element(screen.getByLabelText('Authentication code')).toBeVisible()
    api.failNext('POST', /session\/mfa$/, 401, 'invalid_mfa_ticket', 'Expired.')
    await userEvent.fill(field('mfa-code'), '123456')
    await userEvent.click(submitOf('mfa-code'))

    await expect.element(screen.getByText('That sign in expired. Sign in again.')).toBeVisible()
    await expect.element(screen.getByLabelText('Password')).toBeVisible()
  })

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode on the two step verification step`, async () => {
      setMode(theme, 'comfortable')
      api.consoleMfa = ['totp', 'recovery_code', 'passkey']
      const { screen } = await signInWithPassword()
      await expect
        .element(screen.getByRole('heading', { name: 'Two step verification' }))
        .toBeVisible()

      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })

      expect(results.violations.map((violation) => violation.id)).toEqual([])
    })
  }

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode with the setup notice showing`, async () => {
      setMode(theme, 'comfortable')
      api.setupRequired = true
      const { screen } = await renderApp('/sign-in')
      await expect.element(screen.getByText('Finish setting up Orvano')).toBeVisible()

      const results = await axe.run(document.body, { rules: { region: { enabled: false } } })

      expect(results.violations.map((violation) => violation.id)).toEqual([])
    })
  }
})
