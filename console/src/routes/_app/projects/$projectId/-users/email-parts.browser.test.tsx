import { OrvanoError } from '@orvano/console-client'
import type { User } from '@orvano/console-client'
import axe from 'axe-core'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { render } from 'vitest-browser-react'

import { renderInRouter } from '@/test/router'
import { settleStyles } from '@/test/settle'

import { EmailCard, VerifiedBadge, sessionMethodLabel, verifiedLine } from './email-parts'
import { linkUrlKey, suggestedLinkUrls } from './link-url'

// Spec 0010 AC-22 and AC-23: the verified state, the email actions for every role, the send
// dialogs with the remembered URL, and the change email dialog, at WCAG AA in both themes.

const projectId = 'emailproject0001'
const unverified: User = {
  id: '01a0e581-281d-72e8-b56a-55ef6b0e8c13',
  email: 'ada@example.com',
  emailVerified: false,
  emailVerifiedAt: null,
  name: 'Ada',
  status: 'active',
  metadata: {},
  createdAt: '2026-09-27T00:00:00Z',
  lastSignInAt: null,
  providers: [],
  hasPassword: true,
  mfaEnabled: false,
}
const verified: User = {
  ...unverified,
  emailVerified: true,
  emailVerifiedAt: '2026-09-28T09:30:00Z',
}
const paths = ['/projects/$projectId/users/$userId', '/projects/$projectId/email/settings']
const at = `/projects/${projectId}/users/${unverified.id}`

async function noAxeViolations(): Promise<void> {
  for (const theme of ['dark', 'light']) {
    document.documentElement.dataset.theme = theme
    document.documentElement.style.colorScheme = theme
    // The theme change animates colors and overlays fade in; axe reads colors, so let them finish first.
    await settleStyles()
    const results = await axe.run(document.body, { rules: { region: { enabled: false } } })
    expect(
      results.violations.map(
        (v) => `${v.id}: ${v.nodes.map((n) => n.html.slice(0, 200)).join(' | ')}`,
      ),
    ).toEqual([])
  }
  delete document.documentElement.dataset.theme
  document.documentElement.style.colorScheme = ''
}

function card(
  user: User,
  handlers: Partial<Parameters<typeof EmailCard>[0]> = {},
  reason?: string,
) {
  return (
    <main>
      <EmailCard
        user={user}
        projectId={projectId}
        webHosts={['app.example.com', 'localhost', '*.example.com']}
        disabledReason={reason}
        onSetVerified={vi.fn().mockResolvedValue(undefined)}
        onSendVerification={vi.fn().mockResolvedValue(undefined)}
        onSendRecovery={vi.fn().mockResolvedValue(undefined)}
        onChangeEmail={vi.fn().mockResolvedValue(undefined)}
        {...handlers}
      />
    </main>
  )
}

afterEach(() => {
  window.localStorage.removeItem(linkUrlKey(projectId))
})

describe('the verified state (AC-22)', () => {
  it('shows a word badge, the local date, and each session method in words', async () => {
    const screen = await render(
      <>
        <VerifiedBadge verified />
        <VerifiedBadge verified={false} />
      </>,
    )

    await expect.element(screen.getByText('Verified', { exact: true })).toBeVisible()
    await expect.element(screen.getByText('Unverified')).toBeVisible()
    expect(verifiedLine(unverified)).toBe('Not verified')
    expect(verifiedLine(verified)).toBe(
      `Verified on ${new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date('2026-09-28T09:30:00Z'))}`,
    )
    expect(
      ['password', 'sign_up', 'magic_link', 'email_code', 'recovery'].map((m) =>
        sessionMethodLabel(m as never),
      ),
    ).toEqual(['Password', 'Sign up', 'Magic link', 'Email code', 'Password reset'])
    expect(suggestedLinkUrls(['app.example.com', 'localhost', '*.example.com'])).toEqual([
      'https://app.example.com/',
      'http://localhost/',
    ])
  })
})

describe('EmailCard (AC-23)', () => {
  it('marks an unverified email verified after a confirmation that starts on Cancel', async () => {
    const onSetVerified = vi.fn().mockResolvedValue(undefined)
    const { screen } = await renderInRouter(card(unverified, { onSetVerified }), { at, paths })

    await expect.element(screen.getByText('Not verified')).toBeVisible()
    await noAxeViolations()
    await screen.getByRole('button', { name: 'Mark as verified' }).click()
    const dialog = screen.getByRole('alertdialog')
    await expect.element(dialog.getByRole('button', { name: 'Cancel' })).toHaveFocus()
    await dialog.getByRole('button', { name: 'Mark as verified' }).click()

    await expect.poll(() => onSetVerified.mock.calls).toEqual([[true]])
  })

  it('offers the verification email only while unverified, and Mark as unverified once verified', async () => {
    const { screen } = await renderInRouter(card(verified), { at, paths })

    await expect.element(screen.getByRole('button', { name: 'Mark as unverified' })).toBeVisible()
    await expect
      .element(screen.getByRole('button', { name: 'Send verification email' }))
      .not.toBeInTheDocument()
    await expect
      .element(screen.getByRole('button', { name: 'Send password reset email' }))
      .toBeVisible()
  })

  it('tells a viewer every action is for developers and owners', async () => {
    const onSetVerified = vi.fn()
    const { screen } = await renderInRouter(
      card(unverified, { onSetVerified }, 'Developers and owners only'),
      { at, paths },
    )

    for (const name of [
      'Mark as verified',
      'Send verification email',
      'Send password reset email',
      'Change email',
    ]) {
      await expect
        .element(screen.getByRole('button', { name }))
        .toHaveAccessibleDescription('Developers and owners only')
    }
    await screen.getByRole('button', { name: 'Mark as verified' }).click({ force: true })
    expect(onSetVerified).not.toHaveBeenCalled()
    await noAxeViolations()
  })

  it('sends a reset to the remembered URL, shows a refused URL under the field, and remembers the one that worked', async () => {
    window.localStorage.setItem(linkUrlKey(projectId), 'https://app.example.com/old')
    const onSendRecovery = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(400, 'redirect_url_not_allowed', 'raw', null))
      .mockResolvedValueOnce(undefined)
    const { screen } = await renderInRouter(card(unverified, { onSendRecovery }), { at, paths })

    await screen.getByRole('button', { name: 'Send password reset email' }).click()
    const dialog = screen.getByRole('dialog')
    const field = dialog.getByLabelText('Link opens at')
    await expect.element(field).toHaveValue('https://app.example.com/old')
    await expect
      .element(dialog.getByText(/app\.example\.com, localhost, \*\.example\.com/))
      .toBeVisible()
    await noAxeViolations()

    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect
      .element(dialog.getByText(/Use a URL on one of the project's web platforms/))
      .toBeVisible()
    await expect.element(field).toHaveAttribute('aria-invalid', 'true')

    await field.fill('https://app.example.com/auth/callback')
    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect.element(screen.getByRole('dialog')).not.toBeInTheDocument()
    expect(onSendRecovery).toHaveBeenLastCalledWith('https://app.example.com/auth/callback')
    expect(window.localStorage.getItem(linkUrlKey(projectId))).toBe(
      'https://app.example.com/auth/callback',
    )
  })

  it('explains a missing email server with a link to Email settings, and a limit with when to try again', async () => {
    const onSendVerification = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(409, 'email_not_configured', 'raw', null))
      .mockRejectedValueOnce(new OrvanoError(429, 'rate_limited', 'raw', null, 42))
    const { screen } = await renderInRouter(card(unverified, { onSendVerification }), { at, paths })

    await screen.getByRole('button', { name: 'Send verification email' }).click()
    const dialog = screen.getByRole('dialog')
    await dialog.getByLabelText('Link opens at').fill('com.acme.app://auth')
    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect.element(dialog.getByText('No email server is set up.')).toBeVisible()
    await expect
      .element(dialog.getByRole('link', { name: 'Email settings' }))
      .toHaveAttribute('href', `/projects/${projectId}/email/settings`)
    await noAxeViolations()

    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect.element(dialog.getByText('Try again in 42 seconds.')).toBeVisible()
  })

  it('says in words when the user is blocked or the email was verified meanwhile', async () => {
    const onSendRecovery = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(403, 'user_blocked', 'raw', null))
    const onSendVerification = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(409, 'email_already_verified', 'raw', null))
    const { screen } = await renderInRouter(
      card(unverified, { onSendRecovery, onSendVerification }),
      { at, paths },
    )

    await screen.getByRole('button', { name: 'Send password reset email' }).click()
    let dialog = screen.getByRole('dialog')
    await dialog.getByLabelText('Link opens at').fill('https://app.example.com/auth')
    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect.element(dialog.getByText('This user is blocked.')).toBeVisible()
    await expect
      .element(dialog.getByText('Unblock them first, then send the password reset email.'))
      .toBeVisible()
    await noAxeViolations()
    await dialog.getByRole('button', { name: 'Cancel' }).click()

    await screen.getByRole('button', { name: 'Send verification email' }).click()
    dialog = screen.getByRole('dialog')
    await dialog.getByLabelText('Link opens at').fill('https://app.example.com/auth')
    await dialog.getByRole('button', { name: 'Send email' }).click()
    await expect.element(dialog.getByText('This email is already verified.')).toBeVisible()
  })

  it('changes the email, unverified unless the box is checked, and shows a taken address under the field', async () => {
    const onChangeEmail = vi
      .fn()
      .mockRejectedValueOnce(new OrvanoError(409, 'email_already_in_use', 'raw', null))
      .mockResolvedValueOnce(undefined)
    const { screen } = await renderInRouter(card(unverified, { onChangeEmail }), { at, paths })

    await screen.getByRole('button', { name: 'Change email' }).click()
    const dialog = screen.getByRole('dialog')
    const checkbox = dialog.getByRole('checkbox', { name: 'Mark the new email as verified' })
    await expect.element(checkbox).not.toBeChecked()
    await dialog.getByRole('button', { name: 'Change email' }).click()
    await expect.element(dialog.getByText('Enter an email.')).toBeVisible()
    await noAxeViolations()

    await dialog.getByLabelText('New email').fill('bob@example.com')
    await dialog.getByRole('button', { name: 'Change email' }).click()
    await expect
      .element(dialog.getByText('Another user of this project has this email.'))
      .toBeVisible()

    await dialog.getByLabelText('New email').fill('ada.king@example.com')
    await checkbox.click()
    await dialog.getByRole('button', { name: 'Change email' }).click()
    await expect.element(screen.getByRole('dialog')).not.toBeInTheDocument()
    expect(onChangeEmail).toHaveBeenLastCalledWith({
      email: 'ada.king@example.com',
      emailVerified: true,
    })
  })
})
