import type { AuthMethodSettings, Platform } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { settleStyles } from '@/test/settle'

import { AnonymousCard, PasskeysCard, TotpCard } from './method-cards'
import { PasskeysDialog } from './passkeys-dialog'

// Spec 0013 AC-43 (and AC-2): the Authenticator app and Passkeys cards on the Sign in methods page,
// for writers and viewers, at WCAG AA.

const settings: AuthMethodSettings = {
  totpEnabled: true,
  passkeysEnabled: true,
  rpId: 'example.com',
  rpName: null,
  androidCertFingerprints: ['AA:BB'],
  activePasskeyCount: 3,
  acceptedOrigins: ['https://example.com', 'https://app.example.com', 'https://*.example.com'],
  mfaRequired: false,
  activeUsersWithoutMfa: 2,
  anonymousEnabled: false,
  anonymousIdleDays: 30,
}

const at = '2026-10-08T00:00:00Z'
const platforms: Platform[] = [
  {
    id: 'p1',
    type: 'ios',
    name: 'Shop',
    identifier: 'com.acme.shop',
    createdAt: at,
    updatedAt: at,
  },
  {
    id: 'p2',
    type: 'android',
    name: 'Shop',
    identifier: 'com.acme.shop',
    createdAt: at,
    updatedAt: at,
  },
]

async function noAxeViolations(): Promise<void> {
  await settleStyles()
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

async function renderDialog(
  onSave = vi.fn().mockResolvedValue(undefined),
  readOnlyReason?: string,
) {
  const screen = await render(
    <PasskeysDialog
      settings={settings}
      projectName="Shop"
      platforms={platforms}
      open
      onOpenChange={() => undefined}
      readOnlyReason={readOnlyReason}
      onSave={onSave}
    />,
  )
  return { screen, onSave }
}

describe('TotpCard', () => {
  it('shows the state as a word and saves the switch at once', async () => {
    const onToggle = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <TotpCard
          projectId="shop"
          settings={settings}
          readOnlyReason={undefined}
          onToggle={onToggle}
          onRequireMfa={vi.fn()}
        />
      </main>,
    )

    await expect.element(screen.getByText('On', { exact: true })).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('switch', { name: 'Enabled' }).click()

    await expect.poll(() => onToggle.mock.calls.length).toBe(1)
    expect(onToggle).toHaveBeenCalledWith(false)
  })

  it('keeps a viewer from changing it and says why', async () => {
    const onToggle = vi.fn()
    const screen = await render(
      <main>
        <TotpCard
          projectId="shop"
          settings={settings}
          readOnlyReason="Needs the developer role"
          onToggle={onToggle}
          onRequireMfa={vi.fn()}
        />
      </main>,
    )

    await expect.element(screen.getByRole('switch', { name: 'Enabled' })).toBeDisabled()
    await expect.element(screen.getByRole('switch', { name: 'Require MFA' })).toBeDisabled()
    await expect.element(screen.getByText('Needs the developer role').first()).toBeVisible()
    await noAxeViolations()
  })
})

// Spec 0014 AC-35: the Require MFA switch on the MFA card, with the count of signed in users who
// have no factor, disabled with a reason while no factor can be enrolled.
describe('Require MFA', () => {
  it('saves the switch at once and shows how many signed in users have no factor', async () => {
    const onRequireMfa = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <TotpCard
          projectId="shop"
          settings={settings}
          readOnlyReason={undefined}
          onToggle={vi.fn()}
          onRequireMfa={onRequireMfa}
        />
      </main>,
    )

    await expect
      .element(screen.getByText('2 signed in users have no second factor yet.'))
      .toBeVisible()
    await noAxeViolations()
    await screen.getByRole('switch', { name: 'Require MFA' }).click()

    await expect.poll(() => onRequireMfa.mock.calls.length).toBe(1)
    expect(onRequireMfa).toHaveBeenCalledWith(true)
  })

  it('is disabled with a reason while neither the authenticator app nor passkeys are on', async () => {
    const screen = await render(
      <main>
        <TotpCard
          projectId="shop"
          settings={{ ...settings, totpEnabled: false, passkeysEnabled: false }}
          readOnlyReason={undefined}
          onToggle={vi.fn()}
          onRequireMfa={vi.fn()}
        />
      </main>,
    )

    await expect.element(screen.getByRole('switch', { name: 'Require MFA' })).toBeDisabled()
    await expect
      .element(screen.getByText(/Turn on the authenticator app or passkeys first/))
      .toBeVisible()
    await noAxeViolations()
  })

  it('shows why a save failed', async () => {
    const onRequireMfa = vi.fn().mockRejectedValue(new Error('Network down'))
    const screen = await render(
      <main>
        <TotpCard
          projectId="shop"
          settings={settings}
          readOnlyReason={undefined}
          onToggle={vi.fn()}
          onRequireMfa={onRequireMfa}
        />
      </main>,
    )

    await screen.getByRole('switch', { name: 'Require MFA' }).click()
    await expect.element(screen.getByRole('alert')).toBeVisible()
    await noAxeViolations()
  })
})

describe('PasskeysCard', () => {
  it('names the RP ID and how many passkeys can sign in', async () => {
    const onOpen = vi.fn()
    const screen = await render(
      <main>
        <PasskeysCard settings={settings} onOpen={onOpen} />
      </main>,
    )

    await expect.element(screen.getByText('On example.com: 3 passkeys can sign in')).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'Edit passkeys' }).click()
    expect(onOpen).toHaveBeenCalledOnce()
  })
})

describe('PasskeysDialog', () => {
  it('shows the accepted origins and the files to serve on the RP ID', async () => {
    const { screen } = await renderDialog()

    const dialog = screen.getByRole('dialog', { name: 'Passkeys' })
    await expect.element(dialog.getByText('https://app.example.com')).toBeVisible()
    await expect.element(dialog.getByText('(any one subdomain)')).toBeVisible()
    await expect.element(dialog.getByText(/"<TeamID>\.com\.acme\.shop"/)).toBeVisible()
    await expect.element(dialog.getByText(/common\.get_login_creds/)).toBeVisible()
    await expect
      .element(dialog.getByRole('button', { name: 'Copy apple-app-site-association' }))
      .toBeVisible()
    await expect.element(dialog.getByRole('button', { name: 'Copy assetlinks.json' })).toBeVisible()
    await expect.element(dialog.getByLabelText('RP name')).toHaveAttribute('placeholder', 'Shop')
    await noAxeViolations()
  })

  it('saves the settings, reading fingerprints one per line', async () => {
    const { screen, onSave } = await renderDialog()
    const dialog = screen.getByRole('dialog', { name: 'Passkeys' })

    await dialog.getByLabelText('Android certificate fingerprints').fill('AA:BB\ncc:dd')
    await dialog.getByRole('button', { name: 'Save' }).click()

    await expect.poll(() => onSave.mock.calls.length).toBe(1)
    expect(onSave).toHaveBeenCalledWith({
      passkeysEnabled: true,
      rpId: 'example.com',
      rpName: null,
      androidCertFingerprints: ['AA:BB', 'cc:dd'],
    })
  })

  it('asks for the new RP ID again when passkeys would stop working (AC-2)', async () => {
    const { screen, onSave } = await renderDialog()
    const dialog = screen.getByRole('dialog', { name: 'Passkeys' })

    await dialog.getByLabelText('RP ID').fill('shop.example.com')
    await dialog.getByRole('button', { name: 'Save' }).click()
    const confirm = screen.getByRole('alertdialog')
    await expect.element(confirm.getByText(/3 passkeys made for example.com/)).toBeVisible()
    expect(onSave).not.toHaveBeenCalled()
    await confirm.getByLabelText(/Type shop.example.com to confirm/).fill('shop.example.com')
    await confirm.getByRole('button', { name: 'Change RP ID' }).click()

    await expect.poll(() => onSave.mock.calls.length).toBe(1)
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ rpId: 'shop.example.com', confirmRpIdChange: true }),
    )
  })

  it('shows a viewer every setting, disabled', async () => {
    const { screen } = await renderDialog(undefined, 'Needs the developer role')
    const dialog = screen.getByRole('dialog', { name: 'Passkeys' })

    await expect.element(dialog.getByLabelText('RP ID')).toBeDisabled()
    await expect
      .element(dialog.getByRole('button', { name: 'Save' }))
      .toHaveAttribute('aria-disabled', 'true')
    await noAxeViolations()
  })
})

// Spec 0014 AC-35: the Anonymous users card, its switch and idle days saved together, with the idle
// days checked before a save, and disabled for a viewer.
describe('AnonymousCard', () => {
  it('saves the switch and the idle days together', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <AnonymousCard settings={settings} readOnlyReason={undefined} onSave={onSave} />
      </main>,
    )

    await expect.element(screen.getByText('Off', { exact: true })).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('switch', { name: 'Allow guest sign in' }).click()
    const days = screen.getByRole('textbox', { name: 'Delete idle guests after' })
    await days.fill('0')
    await screen.getByRole('button', { name: 'Save' }).click()
    await expect
      .element(screen.getByText('Enter a whole number of days from 1 to 365.'))
      .toBeVisible()
    expect(onSave).not.toHaveBeenCalled()
    await noAxeViolations()

    await days.fill('14')
    await screen.getByRole('button', { name: 'Save' }).click()
    await expect.poll(() => onSave.mock.calls.length).toBe(1)
    expect(onSave).toHaveBeenCalledWith({ anonymousEnabled: true, anonymousIdleDays: 14 })
  })

  it('keeps a viewer from changing it and says why', async () => {
    const screen = await render(
      <main>
        <AnonymousCard
          settings={{ ...settings, anonymousEnabled: true }}
          readOnlyReason="Needs the developer role"
          onSave={vi.fn()}
        />
      </main>,
    )

    await expect.element(screen.getByText('On', { exact: true })).toBeVisible()
    await expect.element(screen.getByRole('switch', { name: 'Allow guest sign in' })).toBeDisabled()
    await expect
      .element(screen.getByRole('textbox', { name: 'Delete idle guests after' }))
      .toBeDisabled()
    await expect.element(screen.getByText('Needs the developer role').first()).toBeVisible()
    await noAxeViolations()
  })
})
