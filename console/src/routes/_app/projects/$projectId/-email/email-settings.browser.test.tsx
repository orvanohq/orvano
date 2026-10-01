import axe from 'axe-core'
import { afterEach, describe, expect, it } from 'vitest'
import { userEvent } from 'vitest/browser'

import { renderApp, setMode } from '@/test/app'
import { installFakeApi, makeOrg, makeProject, type FakeApi } from '@/test/fake-api'
import type { OrgRole, SmtpSettings } from '@orvano/console-client'

// Spec 0009, AC-1 to AC-6, AC-24, and AC-30: the Email Settings tab, against the fake API.

const api: FakeApi = installFakeApi()
const projectId = 'proj0000000000000001'
const settingsPath = `/projects/${projectId}/email/settings`
const smtpPath = /\/v1\/console\/project\/email\/smtp$/
const testPath = /\/v1\/console\/project\/email\/smtp\/test$/

const installSmtp: SmtpSettings = {
  host: 'smtp.install.test',
  port: 587,
  security: 'starttls',
  username: 'install',
  hasPassword: true,
  fromEmail: 'orvano@example.com',
  fromName: 'Orvano',
  replyTo: null,
  updatedAt: '2026-06-01T10:00:00.000Z',
}

const projectSmtp: SmtpSettings = {
  host: 'smtp.shop.test',
  port: 465,
  security: 'tls',
  username: 'apikey',
  hasPassword: true,
  fromEmail: 'hello@shop.test',
  fromName: 'Shop',
  replyTo: 'support@shop.test',
  updatedAt: '2026-06-02T10:00:00.000Z',
}

function seed({
  role = 'developer',
  install = null,
  project = null,
}: { role?: OrgRole; install?: SmtpSettings | null; project?: SmtpSettings | null } = {}) {
  api.orgs = [makeOrg({ id: 'org00000000000000001', role })]
  api.projects = [makeProject({ id: projectId, orgId: 'org00000000000000001', name: 'Shop' })]
  api.installSmtp = install
  api.projectSmtp = project
  api.requests = []
}

const text = () => document.body.textContent
const button = (name: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find(
    (element) => element.textContent === name || element.getAttribute('aria-label') === name,
  )
const reason = (element: Element | undefined) => {
  const id = element?.getAttribute('aria-describedby')
  return id === null || id === undefined ? undefined : document.getElementById(id)?.textContent
}
const input = (name: string) => document.getElementById(`smtp-${name}`) as HTMLInputElement | null
/** The error shown under a field, which is an alert so it is announced (AC-30). */
const fieldError = (name: string) => {
  const error = document.getElementById(`smtp-${name}-error`)
  return error === null ? undefined : { text: error.textContent, role: error.getAttribute('role') }
}
const sent = (method: string, path: RegExp) =>
  api.requests.filter((request) => request.method === method && path.test(request.path))
const confirm = () => document.querySelector('[role=alertdialog]')

let unmount: (() => Promise<void>) | undefined

/** Renders the Settings tab, in place of any earlier render in the same test, and waits for the form. */
async function openSettings(options?: Parameters<typeof seed>[0]) {
  await unmount?.()
  seed(options)
  const app = await renderApp(settingsPath)
  unmount = () => app.screen.unmount()
  await expect.poll(() => input('host')).not.toBeNull()
  return app
}

afterEach(() => {
  unmount = undefined
  setMode('dark', 'compact')
})

describe('the Email Settings tab', () => {
  // AC-4 and Screens: the Email entry opens Settings, and the install's sender shows above an empty form.
  it('opens from Email and says it uses the install’s server when the project has none', async () => {
    seed({ install: installSmtp })
    const { router, screen } = await renderApp(`/projects/${projectId}/email`)
    await expect.poll(() => router.state.location.pathname).toBe(settingsPath)
    await expect.poll(() => input('host')).not.toBeNull()
    await expect.poll(() => document.title).toBe('Email settings · Shop · Orvano')
    expect(
      document.querySelector('nav[aria-label="Email sections"] [aria-current=page]')?.textContent,
    ).toBe('Settings')

    expect(text()).toContain('Using this server’s email settings')
    expect(text()).toContain('Emails are sent as Orvano <orvano@example.com>.')
    expect(text()).not.toContain('smtp.install.test')
    // An empty form starts on STARTTLS with Port empty, and Save waits for a change.
    expect(input('host')?.value).toBe('')
    expect(input('port')?.value).toBe('')
    await expect
      .element(screen.getByRole('radio', { name: 'STARTTLS (usually port 587)' }))
      .toBeChecked()
    expect(input('password')?.placeholder).toBe('')
    expect(reason(button('Save'))).toBe('Nothing to save yet')
    expect(button('Stop using these settings')).toBeUndefined()
  })

  it('says no email server is set up when neither the project nor the install has one', async () => {
    await openSettings()
    expect(text()).toContain('No email server is set up')
    expect(text()).toContain(
      'Auth emails can’t be sent until you add one here or the install admin adds one for the whole server.',
    )
  })

  // AC-1 and Screens: Security fills Port only while it is empty; Save stores the project's own settings.
  it('fills the usual port from Security while Port is empty, and saves the settings', async () => {
    const { screen } = await openSettings({ install: installSmtp })

    await screen.getByRole('radio', { name: 'TLS (usually port 465)' }).click()
    expect(input('port')?.value).toBe('465')
    await screen.getByRole('radio', { name: 'None' }).click()
    expect(input('port')?.value).toBe('465')
    expect(text()).toContain('Nothing is encrypted, so a username and password can’t be used.')
    await screen.getByLabelText('Port').fill('')
    await screen.getByRole('radio', { name: 'STARTTLS (usually port 587)' }).click()
    expect(input('port')?.value).toBe('587')

    await screen.getByLabelText('Host').fill(' smtp.shop.test ')
    await screen.getByLabelText('Username').fill('apikey')
    await screen.getByLabelText('Password').fill('s3cret')
    await screen.getByLabelText('From email').fill('hello@shop.test')
    await screen.getByLabelText('From name').fill('Shop')
    expect(reason(button('Save'))).toBeUndefined()
    await userEvent.click(button('Save') ?? document.body)

    await expect.poll(text).toContain('Email settings saved')
    expect(sent('PUT', smtpPath).map((request) => request.body)).toEqual([
      {
        host: 'smtp.shop.test',
        port: 587,
        security: 'starttls',
        username: 'apikey',
        password: 's3cret',
        fromEmail: 'hello@shop.test',
        fromName: 'Shop',
        replyTo: null,
      },
    ])
    // The project's own settings now: the install line is gone, the password is never shown again,
    // and the form starts over from what was saved.
    await expect.poll(() => button('Stop using these settings')).toBeDefined()
    expect(text()).not.toContain('Using this server’s email settings')
    expect(input('password')?.value).toBe('')
    expect(input('password')?.placeholder).toBe('Saved. Leave empty to keep it.')
    expect(input('host')?.value).toBe('smtp.shop.test')
    expect(reason(button('Save'))).toBe('Nothing to save yet')
  })

  // AC-6: a test sends the form's current values, saved or not, and saves nothing.
  it('sends a test with the unsaved values and says where it went', async () => {
    const { screen } = await openSettings({ project: projectSmtp })
    await screen.getByLabelText('From name').fill('Shop test')
    await userEvent.click(button('Send test email') ?? document.body)

    await expect.poll(text).toContain('Sent to ada@example.com. Check your inbox.')
    expect(document.querySelector('[role=status]')?.textContent).toContain('Test email sent')
    expect(sent('POST', testPath).map((request) => request.body)).toEqual([
      {
        host: 'smtp.shop.test',
        port: 465,
        security: 'tls',
        username: 'apikey',
        // Empty: the server uses the stored one for the same host, port, and username (AC-1).
        password: null,
        fromEmail: 'hello@shop.test',
        fromName: 'Shop test',
        replyTo: 'support@shop.test',
      },
    ])
    expect(sent('PUT', smtpPath)).toEqual([])
  })

  // AC-2, AC-3, AC-6, AC-30: field errors show under their field; anything else in the alert; all announced.
  it('shows what is refused under the field it names, and anything else in the alert', async () => {
    const { screen } = await openSettings()

    // The form's own check, before asking the server (Save waits for a change; a test doesn't).
    await userEvent.click(button('Send test email') ?? document.body)
    await expect.poll(() => fieldError('host')?.text).toBe('Enter the SMTP host')
    expect(fieldError('host')?.role).toBe('alert')
    expect(fieldError('port')?.text).toBe('Enter a port from 1 to 65535')
    expect(fieldError('fromEmail')?.text).toBe('Enter the address emails are sent from')
    expect(input('host')?.getAttribute('aria-invalid')).toBe('true')
    expect(input('host')?.getAttribute('aria-describedby')).toContain('smtp-host-error')
    expect(api.requests.filter((request) => request.method !== 'GET')).toEqual([])

    await screen.getByLabelText('Host').fill('smtp.shop.test')
    await screen.getByLabelText('Port').fill('587')
    await screen.getByLabelText('From email').fill('hello@shop')

    // A field the server names, after its camelCase name and a colon.
    api.failNext('PUT', smtpPath, 400, 'invalid_request', 'fromEmail: Enter a valid email address')
    await userEvent.click(button('Save') ?? document.body)
    await expect.poll(() => fieldError('fromEmail')?.text).toBe('Enter a valid email address')
    expect(fieldError('host')).toBeUndefined()
    // Typing clears it.
    await screen.getByLabelText('From email').fill('hello@shop.test')
    expect(fieldError('fromEmail')).toBeUndefined()

    // A host on a private network shows under Host.
    api.failNext(
      'POST',
      testPath,
      400,
      'smtp_host_not_allowed',
      'The SMTP host points to a private network address, which projects can’t use.',
    )
    await userEvent.click(button('Send test email') ?? document.body)
    await expect
      .poll(() => fieldError('host')?.text)
      .toBe('The SMTP host points to a private network address, which projects can’t use.')

    // An SMTP outcome belongs to no field: the alert says so.
    api.failNext(
      'POST',
      testPath,
      502,
      'smtp_auth_failed',
      'The SMTP server refused the username or password.',
    )
    await userEvent.click(button('Send test email') ?? document.body)
    await expect
      .poll(() => document.querySelector('form [role=alert]:not([id])')?.textContent)
      .toBe("Couldn't send the test emailThe SMTP server refused the username or password.")
    expect(fieldError('host')).toBeUndefined()
  })

  // AC-5: the confirm says what happens next, starts on Cancel, and the tab shows the new source.
  it.each([
    [installSmtp, 'Emails will use this server’s settings.', 'Using this server’s email settings'],
    [null, 'This project won’t be able to send email.', 'No email server is set up'],
  ] as const)(
    'stops using the project’s settings after a confirmation (install: %#)',
    async (install, description, after) => {
      await openSettings({ install, project: projectSmtp })
      expect(input('host')?.value).toBe('smtp.shop.test')

      await userEvent.click(button('Stop using these settings') ?? document.body)
      await expect.poll(() => confirm()?.textContent).toContain(description)
      const cancel = [...(confirm()?.querySelectorAll('button') ?? [])].find(
        (element) => element.textContent === 'Cancel',
      )
      await expect.poll(() => document.activeElement).toBe(cancel)
      const stop = [...(confirm()?.querySelectorAll('button') ?? [])].find(
        (element) => element.textContent === 'Stop using these settings',
      )
      await userEvent.click(stop ?? document.body)

      await expect.poll(text).toContain(after)
      expect(sent('DELETE', smtpPath)).toHaveLength(1)
      expect(input('host')?.value).toBe('')
      expect(button('Stop using these settings')).toBeUndefined()
    },
  )

  // AC-24: a viewer reads the same form, with no password field and every change disabled.
  it('is read only for a viewer, with no password field', async () => {
    await openSettings({ role: 'viewer', project: projectSmtp })
    expect(input('host')?.value).toBe('smtp.shop.test')
    expect(input('host')?.readOnly).toBe(true)
    expect(input('fromEmail')?.readOnly).toBe(true)
    expect(input('password')).toBeNull()
    for (const name of ['Save', 'Send test email', 'Stop using these settings']) {
      expect(button(name)?.getAttribute('aria-disabled'), name).toBe('true')
      expect(reason(button(name)), name).toBe('Developers and owners only')
    }
    expect(api.requests.filter((request) => request.method !== 'GET')).toEqual([])
  })

  // AC-30: every state of the tab, in both themes and densities.
  it.each([
    ['dark', 'compact'],
    ['light', 'compact'],
    ['dark', 'comfortable'],
    ['light', 'comfortable'],
  ] as const)('has no axe violations in %s, %s', async (theme, density) => {
    setMode(theme, density)
    await openSettings({ install: installSmtp })
    expect((await axe.run(document.body)).violations.map((violation) => violation.id)).toEqual([])

    await openSettings({ project: projectSmtp })
    api.failNext('POST', testPath, 502, 'smtp_unreachable', 'Couldn’t connect to the SMTP server.')
    await userEvent.click(button('Send test email') ?? document.body)
    await expect.poll(text).toContain('Couldn’t connect to the SMTP server.')
    expect((await axe.run(document.body)).violations.map((violation) => violation.id)).toEqual([])

    await openSettings({ role: 'viewer' })
    expect((await axe.run(document.body)).violations.map((violation) => violation.id)).toEqual([])
  })
})
