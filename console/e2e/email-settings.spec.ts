import type { Page } from '@playwright/test'

import { consoleUser, cookieHeader, expect, scenariosProject, test } from './fixtures.ts'

// Spec 0009 AC-1, AC-2, AC-4 to AC-6 against the real API, through the UI only: the fixture owner
// sees the project using the install's SMTP, points the project at Mailpit, sends a test that
// arrives from the new sender, sees a refused field and an unreachable server in words, saves, and
// stops using the settings again.

const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025'
const settings = `/projects/${scenariosProject}/email/settings`

/** A text field of the SMTP form by its label (the radio labels also say "port"). */
const field = (page: Page, name: string) => page.getByRole('textbox', { name, exact: true })

interface MailpitSearch {
  messages: { ID: string }[]
}

interface MailpitMessage {
  Subject: string
  From: { Name: string; Address: string }
  To: { Address: string }[]
  Text: string
  HTML: string
}

test('an owner sets project SMTP, sends a test that arrives, saves, and stops using it', async ({
  signedIn: page,
  request,
  baseURL,
  session,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  // Start from the install's settings, even after an earlier run that stopped halfway.
  const reset = await request.delete(`${baseURL}/v1/console/project/email/smtp`, {
    headers: {
      cookie: cookieHeader(session),
      'sec-fetch-site': 'same-origin',
      'X-Orvano-Project': scenariosProject,
    },
  })
  expect(reset.status()).toBe(204)
  const from = `e2e-${String(Date.now())}@scenarios.test`

  // The Email entry opens Settings, which uses the install's server (AC-4).
  await page.goto(`/projects/${scenariosProject}/email`)
  await expect(page).toHaveURL(new RegExp(`${settings}$`))
  await expect(page).toHaveTitle('Email settings · Scenarios · Orvano')
  await expect(page.getByText('Using this server’s email settings')).toBeVisible()
  await expect(page.getByText('Emails are sent as orvano@scenarios.test.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Stop using these settings' })).toHaveCount(0)

  // None fills the usual port while Port is empty; Mailpit listens on 1025.
  await field(page, 'Host').fill('mailpit')
  await page.getByRole('radio', { name: 'None' }).click()
  await expect(field(page, 'Port')).toHaveValue('25')
  await field(page, 'Port').fill('1025')
  await field(page, 'From name').fill('E2E Shop')

  // The server's own rule names the field, and the form shows it there (AC-2).
  await field(page, 'From email').fill('not an address')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.locator('#smtp-fromEmail-error')).toBeVisible()
  await expect(field(page, 'From email')).toHaveAttribute('aria-invalid', 'true')
  await field(page, 'From email').fill(from)
  await expect(page.locator('#smtp-fromEmail-error')).toHaveCount(0)

  // A server that isn't listening fails in words, in the form's alert (AC-6).
  await field(page, 'Port').fill('1')
  await page.getByRole('button', { name: 'Send test email' }).click()
  await expect(page.getByRole('alert').filter({ hasText: 'send the test email' })).toBeVisible()
  await field(page, 'Port').fill('1025')

  // A test with the unsaved values arrives in Mailpit, from the new sender, to the caller (AC-6).
  await page.getByRole('button', { name: 'Send test email' }).click()
  await expect(page.getByText(`Sent to ${consoleUser.email}. Check your inbox.`)).toBeVisible()
  let id = ''
  await expect
    .poll(async () => {
      const search = await request.get(
        `${mailpit}/api/v1/search?query=${encodeURIComponent(`from:${from}`)}`,
      )
      id = ((await search.json()) as MailpitSearch).messages[0]?.ID ?? ''
      return id
    })
    .not.toBe('')
  const message = (await (
    await request.get(`${mailpit}/api/v1/message/${id}`)
  ).json()) as MailpitMessage
  expect(message.Subject).toBe('Test email from Orvano')
  expect(message.From).toEqual({ Name: 'E2E Shop', Address: from })
  expect(message.To.map((to) => to.Address)).toEqual([consoleUser.email])
  expect(message.Text).toContain('the project Scenarios')
  expect(message.HTML).toContain('<html lang="en" dir="ltr">')

  // Save: the project's own settings now, kept across a reload (AC-1, AC-4).
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('Email settings saved')).toBeVisible()
  await page.reload()
  await expect(field(page, 'Host')).toHaveValue('mailpit')
  await expect(field(page, 'From email')).toHaveValue(from)
  await expect(page.getByText('Using this server’s email settings')).toHaveCount(0)

  // Stop using them, after a confirm that starts on Cancel and says what happens next (AC-5).
  await page.getByRole('button', { name: 'Stop using these settings' }).click()
  const confirm = page.getByRole('alertdialog', { name: 'Stop using these settings?' })
  await expect(confirm).toContainText('Emails will use this server’s settings.')
  await expect(confirm.getByRole('button', { name: 'Cancel' })).toBeFocused()
  await confirm.getByRole('button', { name: 'Stop using these settings' }).click()
  await expect(page.getByText('Email settings removed')).toBeVisible()
  await expect(page.getByText('Using this server’s email settings')).toBeVisible()
  await expect(field(page, 'Host')).toHaveValue('')
})
