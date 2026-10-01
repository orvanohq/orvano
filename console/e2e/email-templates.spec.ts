import { consoleUser, expect, scenariosProject, test } from './fixtures.ts'

// Spec 0009 AC-9, AC-12, AC-30 against the real API and the production security headers, through
// the UI only: the fixture owner edits the password reset template, watches the preview follow,
// sends a test that arrives in Mailpit, saves, and resets to the default.

const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025'
const editor = `/projects/${scenariosProject}/email/templates/recovery`

interface MailpitSearch {
  messages: { ID: string; Subject: string }[]
}

test('an owner edits a template, previews it, sends a test, saves, and resets', async ({
  signedIn: page,
  request,
}) => {
  await page.addInitScript(() => {
    const seen: string[] = []
    ;(window as unknown as { __csp: string[] }).__csp = seen
    document.addEventListener('securitypolicyviolation', (event) => {
      seen.push(`${event.violatedDirective}: ${event.blockedURI}`)
    })
  })
  const subject = `Reset it ${String(Date.now())}, {{ project.name }}`
  const rendered = subject.replace('{{ project.name }}', 'Scenarios')

  await page.goto(`/projects/${scenariosProject}/email/templates`)
  await expect(page.getByRole('heading', { level: 2, name: 'Templates' })).toBeVisible()
  await page.getByRole('link', { name: 'Password reset' }).click()
  await expect(page).toHaveURL(new RegExp(`${editor}$`))
  await expect(page).toHaveTitle('Password reset template · Scenarios · Orvano')

  // The editors are CodeMirror in a shadow root: labelled, styled, and with no CSP violation.
  const html = page.getByRole('textbox', { name: 'HTML' })
  // CodeMirror draws only the lines near the visible area, so check the top of the document.
  await expect(html).toContainText('<title>Reset your password for {{ project.name }}</title>')
  await expect(page.getByRole('textbox', { name: 'Text' })).toContainText('Reset password:')
  expect(await html.evaluate((node) => getComputedStyle(node).fontFamily)).toContain(
    'JetBrains Mono',
  )

  // The default preview: the sample link, in a frame that can run nothing.
  const frame = page.locator('iframe[title="Email preview"]')
  await expect(frame).toHaveAttribute('sandbox', '')
  const preview = page.frameLocator('iframe[title="Email preview"]')
  await expect(preview.getByRole('link', { name: 'Reset password' })).toHaveAttribute(
    'href',
    'https://example.com/auth/confirm?token=sample',
  )
  await expect(page.getByTestId('preview-subject')).toHaveText('Reset your password for Scenarios')

  // Typing shows in the preview without a save (AC-9).
  await page.getByLabel('Subject').fill(subject)
  // Inserted in one piece, as a paste is, so the editor's tag closing doesn't join in.
  await html.click()
  await page.keyboard.press('ControlOrMeta+a')
  await page.keyboard.insertText('<h1>Hello {{ user.email }}</h1>')
  await expect(preview.getByRole('heading', { level: 1 })).toHaveText(`Hello ${consoleUser.email}`)
  await expect(page.getByTestId('preview-subject')).toHaveText(rendered)

  // An unknown variable shows under the part, with its line (AC-10); undoing it clears the error.
  await page.keyboard.insertText('\n{{ user.emial }}')
  const problem = page.getByRole('alert').filter({ hasText: 'unknown variable user.emial' })
  await expect(problem).toHaveText('Line 2: unknown variable user.emial')
  await page.keyboard.press('ControlOrMeta+z')
  await expect(problem).toHaveCount(0)

  // A test email with the unsaved content arrives through the install's SMTP (AC-12).
  await page.getByRole('button', { name: 'Send test' }).click()
  await expect(page.getByText(`Sent to ${consoleUser.email}. Check your inbox.`)).toBeVisible()
  await expect
    .poll(async () => {
      const search = await request.get(
        `${mailpit}/api/v1/search?query=${encodeURIComponent(`subject:"${rendered}"`)}`,
      )
      return ((await search.json()) as MailpitSearch).messages.length
    })
    .toBe(1)

  // Save, and the template is the project's own; reset brings the default back.
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('Template saved')).toBeVisible()
  await expect(page.getByText('Custom', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Reset to default' }).click()
  const confirm = page.getByRole('alertdialog', { name: 'Reset to the default template?' })
  await expect(confirm.getByRole('button', { name: 'Cancel' })).toBeFocused()
  await confirm.getByRole('button', { name: 'Reset to default' }).click()
  await expect(page.getByText('Template reset')).toBeVisible()
  await expect(page.getByLabel('Subject')).toHaveValue('Reset your password for {{ project.name }}')
  await expect(page.getByRole('textbox', { name: 'HTML' })).toContainText(
    '<title>Reset your password for {{ project.name }}</title>',
  )

  expect(await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp)).toEqual([])
})
