import { OrvanoError } from '@orvano/console-client'
import type { Identity, OAuthProviderSettings } from '@orvano/console-client'
import axe from 'axe-core'
import { describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { IdentitiesTable } from '../-users/identities'
import { SignInMethods } from '../-users/parts'
import { ProviderCard } from './provider-card'
import { ProviderDialog } from './provider-dialog'

// Spec 0012 AC-25 and AC-26: the Sign in methods cards and dialog, a user's identities, and the
// Users list's Sign in column, for every role, at WCAG AA.

const microsoft: OAuthProviderSettings = {
  provider: 'microsoft',
  enabled: true,
  clientId: 'ms-app',
  clientSecretSet: true,
  clientSecretHint: '1a2b',
  clientIdsExtra: [],
  appleTeamId: null,
  appleKeyId: null,
  applePrivateKeySet: false,
  microsoftTenant: 'common',
  redirectReady: true,
  nativeReady: false,
  callbackUrl: 'https://orvano.example.com/v1/projects/p/oauth/microsoft/callback',
  updatedAt: '2026-10-01T00:00:00Z',
}

const identity: Identity = {
  id: 'i1',
  provider: 'github',
  subject: '42',
  email: 'octo@example.com',
  emailVerified: true,
  createdAt: '2026-10-01T10:00:00Z',
  lastSignInAt: null,
}

/** Lets a dialog finish fading in, so axe measures its real colors. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 300))

async function noAxeViolations(): Promise<void> {
  await settle()
  const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
  expect(results.violations.map((violation) => violation.id)).toEqual([])
}

describe('ProviderCard', () => {
  it('shows the state and the ready ways in words', async () => {
    const screen = await render(
      <main>
        <ProviderCard settings={microsoft} onOpen={() => undefined} />
      </main>,
    )
    await expect.element(screen.getByText('On')).toBeVisible()
    await expect.element(screen.getByText('Ready: Redirect')).toBeVisible()
    await expect.element(screen.getByRole('button', { name: 'Edit Microsoft' })).toBeVisible()
    await noAxeViolations()
  })
})

describe('ProviderDialog', () => {
  it('shows a set secret by its last 4, the callback URL, the xms_edov note, and a refusal under its field', async () => {
    const onSave = vi
      .fn()
      .mockRejectedValueOnce(
        new OrvanoError(
          400,
          'invalid_request',
          'microsoftTenant: Enter common, organizations, consumers, or a tenant ID (a GUID).',
          null,
        ),
      )
      .mockResolvedValueOnce(undefined)
    const screen = await render(
      <ProviderDialog
        settings={microsoft}
        open
        onOpenChange={() => undefined}
        readOnlyReason={undefined}
        onSave={onSave}
      />,
    )
    const dialog = screen.getByRole('dialog')

    await expect.element(dialog.getByText('Set, ends in 1a2b')).toBeVisible()
    await expect.element(dialog.getByText(microsoft.callbackUrl)).toBeVisible()
    await expect.element(dialog.getByText(/xms_edov/)).toBeVisible()
    await noAxeViolations()

    await dialog.getByLabelText('Tenant').fill('contoso')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect
      .element(dialog.getByText('Enter common, organizations, consumers, or a tenant ID (a GUID).'))
      .toBeVisible()
    expect(onSave.mock.calls[0]?.[0]).toEqual({
      enabled: true,
      clientId: 'ms-app',
      microsoftTenant: 'contoso',
    })

    await dialog.getByRole('button', { name: 'Clear' }).click()
    await dialog.getByLabelText('Tenant').fill('common')
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect.poll(() => onSave.mock.calls.length).toBe(2)
    expect(onSave.mock.calls[1]?.[0]).toMatchObject({ clientSecret: null })
  })

  it('shows a viewer everything disabled and why', async () => {
    const onSave = vi.fn()
    const screen = await render(
      <ProviderDialog
        settings={microsoft}
        open
        onOpenChange={() => undefined}
        readOnlyReason="Developers and owners only"
        onSave={onSave}
      />,
    )
    const dialog = screen.getByRole('dialog')
    await expect.element(dialog.getByText('Developers and owners only').first()).toBeVisible()
    await expect.element(dialog.getByLabelText('Tenant')).toBeDisabled()
    await expect.element(dialog.getByRole('switch')).toBeDisabled()
    expect(dialog.getByRole('button', { name: 'Replace' }).elements()).toHaveLength(0)
    await noAxeViolations()
  })
})

describe('IdentitiesTable', () => {
  it('lists a provider with its email and unlinks it after a confirmation', async () => {
    const onUnlink = vi.fn().mockResolvedValue(undefined)
    const screen = await render(
      <main>
        <IdentitiesTable
          identities={[identity]}
          loading={false}
          error={undefined}
          onRetry={() => undefined}
          unlinkReason={undefined}
          onUnlink={onUnlink}
        />
      </main>,
    )
    await expect.element(screen.getByText('octo@example.com')).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'Unlink GitHub' }).click()
    await screen.getByRole('alertdialog').getByRole('button', { name: 'Unlink' }).click()
    await expect.poll(() => onUnlink.mock.calls.length).toBe(1)
  })
})

describe('SignInMethods', () => {
  it('names each provider for screen readers, then Password, or Email for a user with neither', async () => {
    const screen = await render(
      <main>
        <SignInMethods user={{ providers: ['google', 'github'], hasPassword: true }} />
        <SignInMethods user={{ providers: [], hasPassword: false }} />
      </main>,
    )
    await expect.element(screen.getByText('Google', { exact: true })).toBeInTheDocument()
    await expect.element(screen.getByText('GitHub', { exact: true })).toBeInTheDocument()
    await expect.element(screen.getByText('Password')).toBeVisible()
    await expect.element(screen.getByText('Email')).toBeVisible()
    await noAxeViolations()
  })
})
