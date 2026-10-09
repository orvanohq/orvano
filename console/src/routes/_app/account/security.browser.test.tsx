import axe from 'axe-core'
import { beforeEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { fakeRecoveryCodes, fakeTotpSecret, installFakeApi, type FakeApi } from '@/test/fake-api'
import { settleStyles } from '@/test/settle'

// Spec 0013 AC-42: the console account's Security page: turning the authenticator app on with a QR
// code and recovery codes, step up before a guarded change (then the change repeats once), and
// passkeys, at WCAG AA in both themes.

const api: FakeApi = installFakeApi()

const text = () => document.body.textContent
const sent = (method: string, path: string) =>
  api.requests.filter((request) => request.method === method && request.path === path)
const dialog = () => document.querySelector<HTMLElement>('[data-slot=dialog-content]')
const field = (id: string) => document.getElementById(id) ?? document.body
const submitOf = (fieldId: string) =>
  document
    .getElementById(fieldId)
    ?.closest('form')
    ?.querySelector<HTMLButtonElement>('button[type=submit]') ?? document.body

const mfaOn = {
  mfaEnabled: true,
  totpConfirmed: true,
  totpConfirmedAt: '2026-06-01T10:00:00.000Z',
  recoveryCodesRemaining: 8,
  passkeyCount: 0,
  factorsAvailable: ['totp', 'passkey'] as const,
}

async function noAxeViolations(): Promise<void> {
  await settleStyles()
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

beforeEach(() => {
  setMode('dark', 'compact')
  api.signedIn = true
  api.requests = []
  api.accountPasskeys = []
  api.accountMfa = {
    mfaEnabled: false,
    totpConfirmed: false,
    totpConfirmedAt: null,
    recoveryCodesRemaining: 0,
    passkeyCount: 0,
    factorsAvailable: ['totp', 'passkey'],
  }
})

describe('/account/security', () => {
  it('turns the authenticator app on with a QR code, then shows the recovery codes once', async () => {
    const { screen } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Turn on' }).click()

    await expect
      .element(screen.getByRole('img', { name: 'QR code for your authenticator app' }))
      .toBeVisible()
    expect(text()).toContain(fakeTotpSecret)
    await expect.element(screen.getByRole('button', { name: 'Copy key' })).toBeVisible()
    await noAxeViolations()
    await userEvent.fill(field('totp-confirm-code'), '123456')
    await userEvent.click(submitOf('totp-confirm-code'))

    await expect.element(screen.getByRole('list', { name: 'Recovery codes' })).toBeVisible()
    for (const code of fakeRecoveryCodes) expect(text()).toContain(code)
    expect(sent('POST', '/v1/console/account/mfa/totp/confirm')[0]?.body).toEqual({
      code: '123456',
    })
    await expect.element(screen.getByRole('button', { name: 'Download' })).toBeVisible()
    await noAxeViolations()

    await screen.getByRole('button', { name: 'I saved them' }).click()
    await expect.element(screen.getByText('10 of 10')).toBeVisible()
    expect(text()).not.toContain(fakeRecoveryCodes[0])
  })

  it('asks for a second factor when Orvano needs one, then turns the app off', async () => {
    api.accountMfa = { ...mfaOn, factorsAvailable: ['totp', 'passkey'] }
    api.failNext('DELETE', /mfa\/totp$/, 403, 'mfa_verification_required', 'Step up first.')
    const { screen } = await renderApp('/account/security')
    await expect.element(screen.getByText('8 of 10')).toBeVisible()

    await screen.getByRole('button', { name: 'Turn off' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Turn off' }).click()

    await expect.poll(dialog).not.toBeNull()
    expect(dialog()?.textContent).toContain("Confirm it's you")
    await noAxeViolations()
    await userEvent.fill(field('step-up-code'), '654321')
    await userEvent.click(submitOf('step-up-code'))

    await expect.poll(() => sent('DELETE', '/v1/console/account/mfa/totp').length).toBe(2)
    expect(sent('POST', '/v1/console/account/mfa/verify')[0]?.body).toEqual({
      totpCode: '654321',
    })
    await expect.element(screen.getByRole('button', { name: 'Turn on' })).toBeVisible()
  })

  it('takes a recovery code at step up', async () => {
    api.accountMfa = { ...mfaOn, factorsAvailable: ['totp', 'passkey'] }
    api.failNext('POST', /recovery-codes$/, 403, 'mfa_verification_required', 'Step up first.')
    const { screen } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Make new codes' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Make new codes' }).click()
    await expect.poll(dialog).not.toBeNull()

    await screen.getByRole('button', { name: 'Use a recovery code' }).click()
    await userEvent.fill(field('step-up-recovery-code'), 'aaaaa-22222')
    await userEvent.click(submitOf('step-up-recovery-code'))

    await expect.element(screen.getByRole('list', { name: 'Recovery codes' })).toBeVisible()
    expect(sent('POST', '/v1/console/account/mfa/verify')[0]?.body).toEqual({
      recoveryCode: 'aaaaa-22222',
    })
  })

  it('offers Sign in again when an account without MFA has an old session and no passkey', async () => {
    api.failNext('POST', /mfa\/totp$/, 403, 'reauthentication_required', 'Sign in again.')
    const { screen, router } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Turn on' }).click()

    await expect.poll(dialog).not.toBeNull()
    expect(document.getElementById('step-up-code')).toBeNull()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'Sign in again' }).click()

    await expect.poll(() => router.state.location.pathname).toBe('/sign-in')
    expect(router.state.location.search).toEqual({ redirect: '/account/security' })
    expect(sent('DELETE', '/v1/console/account/session')).toHaveLength(1)
  })

  it('asks for the password before turning the app on, and again while it is wrong', async () => {
    api.failNext('POST', /mfa\/totp$/, 401, 'invalid_credentials', 'Send your password.')
    api.failNext('POST', /mfa\/totp$/, 401, 'invalid_credentials', 'Wrong password.')
    const { screen } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Turn on' }).click()

    await expect.poll(dialog).not.toBeNull()
    expect(dialog()?.textContent).toContain('Enter your password')
    expect(dialog()?.textContent).not.toContain('That password is wrong.')
    await noAxeViolations()
    await userEvent.click(submitOf('step-up-password'))
    await expect.element(screen.getByText('Enter your password.')).toBeVisible()
    await userEvent.fill(field('step-up-password'), 'not it')
    await userEvent.click(submitOf('step-up-password'))

    await expect.element(screen.getByText('That password is wrong.')).toBeVisible()
    await userEvent.fill(field('step-up-password'), 'correct horse battery staple')
    await userEvent.click(submitOf('step-up-password'))

    await expect
      .element(screen.getByRole('img', { name: 'QR code for your authenticator app' }))
      .toBeVisible()
    expect(sent('POST', '/v1/console/account/mfa/totp').map((request) => request.body)).toEqual([
      {},
      { password: 'not it' },
      { password: 'correct horse battery staple' },
    ])
    await expect.poll(dialog).toBeNull()
  })

  it('asks for the password before adding a passkey, and closing it adds nothing', async () => {
    api.failNext(
      'POST',
      /passkeys\/registration$/,
      401,
      'invalid_credentials',
      'Send your password.',
    )
    const { screen } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Add a passkey' }).click()

    await expect.poll(dialog).not.toBeNull()
    expect(document.getElementById('step-up-password')).not.toBeNull()
    await userEvent.keyboard('{Escape}')

    await expect.poll(dialog).toBeNull()
    expect(sent('POST', '/v1/console/account/passkeys/registration')).toHaveLength(1)
    expect(sent('POST', '/v1/console/account/passkeys')).toHaveLength(0)
  })

  it('closing the step up dialog leaves things as they are', async () => {
    api.accountMfa = { ...mfaOn, factorsAvailable: ['totp', 'passkey'] }
    api.failNext('DELETE', /mfa\/totp$/, 403, 'mfa_verification_required', 'Step up first.')
    const { screen } = await renderApp('/account/security')
    await screen.getByRole('button', { name: 'Turn off' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Turn off' }).click()
    await expect.poll(dialog).not.toBeNull()

    await userEvent.keyboard('{Escape}')

    await expect.poll(dialog).toBeNull()
    expect(sent('DELETE', '/v1/console/account/mfa/totp')).toHaveLength(1)
    await expect.element(screen.getByText('8 of 10')).toBeVisible()
  })

  it('lists, renames, and removes passkeys', async () => {
    api.accountPasskeys = [
      {
        id: 'pk1',
        name: 'Passkey',
        createdAt: '2026-06-01T10:00:00.000Z',
        lastUsedAt: null,
        synced: true,
        active: true,
      },
    ]
    api.accountMfa = { ...api.accountMfa, passkeyCount: 1 }
    const { screen } = await renderApp('/account/security')
    await expect.element(screen.getByRole('cell', { name: 'Synced' })).toBeVisible()

    await screen.getByRole('button', { name: 'Rename passkey Passkey' }).click()
    await userEvent.fill(field('passkey-name'), '  MacBook  ')
    await userEvent.click(submitOf('passkey-name'))
    await expect.element(screen.getByRole('cell', { name: 'MacBook' })).toBeVisible()
    expect(sent('PATCH', '/v1/console/account/passkeys/pk1')[0]?.body).toEqual({
      name: 'MacBook',
    })

    await screen.getByRole('button', { name: 'Remove passkey MacBook' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Remove passkey' }).click()
    await expect.element(screen.getByText('No passkey yet.')).toBeVisible()
  })

  for (const theme of ['light', 'dark'] as const) {
    it(`has no axe violations in ${theme} mode`, async () => {
      setMode(theme, 'comfortable')
      api.accountMfa = { ...mfaOn, factorsAvailable: ['totp', 'passkey'] }
      const { screen } = await renderApp('/account/security')
      await expect.element(screen.getByText('8 of 10')).toBeVisible()
      await noAxeViolations()
    })
  }
})
