import type { FrameLocator, Page } from '@playwright/test'

import { consoleUser, expect, scenariosProject, test } from './fixtures.ts'

// Spec 0009 AC-9, AC-12, AC-30 to AC-33 against the real API and the production security headers,
// through the UI only: the fixture owner edits the password reset template, watches the preview
// follow, sends a test that arrives in Mailpit, saves, and resets to the default; then the preview
// frame's styles, links, and policy.

// One test saves and resets a template the others read.
test.describe.configure({ mode: 'serial' })

const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025'
const editor = `/projects/${scenariosProject}/email/templates/recovery`

interface MailpitSearch {
  messages: { ID: string; Subject: string }[]
}

/** The email itself: the inner frame, inside the frame page. */
const emailOf = (page: Page): FrameLocator =>
  page.frameLocator('iframe[title="Email preview"]').frameLocator('iframe[title="Email content"]')

/**
 * Every Content Security Policy message any frame logs. A page's own listener never sees a frame's
 * violations, and the email's frame runs no script, so these come from the browser's console.
 * Attach it before the first `goto`.
 */
function collectPolicyErrors(page: Page): string[] {
  const seen: string[] = []
  page.on('console', (message) => {
    if (message.text().includes('Content Security Policy')) seen.push(message.text())
  })
  return seen
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

  // The default preview: the sample link, in the frame page's inner frame (AC-31).
  const frame = page.locator('iframe[title="Email preview"]')
  await expect(frame).toHaveAttribute(
    'sandbox',
    'allow-scripts allow-popups allow-popups-to-escape-sandbox',
  )
  const preview = emailOf(page)
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

const kinds = [
  ['verification', 'Verify email'],
  ['recovery', 'Reset password'],
  ['magic_link', 'Sign in'],
] as const

test('each default template previews with its styles and no policy violation (AC-31, AC-32)', async ({
  signedIn: page,
}) => {
  const errors = collectPolicyErrors(page)
  for (const [kind] of [...kinds, ['email_code', '']] as const) {
    await page.goto(`/projects/${scenariosProject}/email/templates/${kind}`)
    const email = emailOf(page)
    await expect(email.locator('h1')).toBeVisible()
    // The preheader is in the HTML, hidden by its inline style.
    await expect(email.locator('body > div > div').first()).toBeHidden()
    expect(errors, kind).toEqual([])
  }

  for (const [kind, button] of kinds) {
    await page.goto(`/projects/${scenariosProject}/email/templates/${kind}`)
    const link = emailOf(page).getByRole('link', { name: button, exact: true })
    await expect(link).toHaveCSS('background-color', 'rgb(24, 24, 27)')
    const box = await link.boundingBox()
    expect(box?.height, kind).toBeGreaterThanOrEqual(44)
    await expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  }

  // The frame page is light whatever the console's theme, and its frames carry their titles.
  const outer = page.frameLocator('iframe[title="Email preview"]')
  await expect(outer.locator('html')).toHaveAttribute('lang', 'en')
  await expect(outer.locator('iframe[title="Email content"]')).toHaveCount(1)
  await expect(outer.locator('body')).toHaveCSS('background-color', 'rgb(255, 255, 255)')
  expect(errors).toEqual([])
})

test('the preview applies inline styles and images, blocks the rest, and opens only safe links (AC-31 to AC-33)', async ({
  signedIn: page,
  baseURL,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  const errors = collectPolicyErrors(page)
  const loads: { url: string; referer: string | undefined }[] = []
  page.on('request', (request) => {
    loads.push({ url: request.url(), referer: request.headers().referer })
  })
  const blocked: string[] = []
  page.on('requestfailed', (request) => {
    if (request.failure()?.errorText === 'csp') blocked.push(request.url())
  })
  const outerLoads: string[] = []
  page.on('framenavigated', (frame) => {
    if (frame.url().endsWith('/frames/email-preview.html')) outerLoads.push(frame.url())
  })

  // The magic link template, so the test that saves the recovery one never meets this one.
  await page.goto(`/projects/${scenariosProject}/email/templates/magic_link`)
  const email = emailOf(page)
  await expect(email.locator('h1')).toBeVisible()
  expect(outerLoads).toHaveLength(1)

  const pixel =
    'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
  const remoteImage = 'https://example.com/orvano-preview-image.png'
  const remoteStyles = 'https://example.com/orvano-preview.css'
  const html = [
    '<!doctype html><html><head>',
    '<meta name="referrer" content="unsafe-url">',
    `<link rel="stylesheet" href="${remoteStyles}">`,
    '<style>.styled { color: rgb(1, 2, 3); }</style>',
    '</head><body>',
    '<p class="styled">Styled</p>',
    `<img alt="Inline pixel" src="${pixel}">`,
    `<img alt="Remote image" src="${remoteImage}">`,
    '<p id="target">Untouched</p>',
    '<script>document.getElementById("target").textContent = "Script ran"</script>',
    '<button onclick="this.textContent = \'Handler ran\'">Press</button>',
    `<a href="${baseURL}/favicon.svg">Safe link</a>`,
    '<a href="javascript:document.title = \'Ran\'">Script link</a>',
    '<a href="/orgs">Relative link</a>',
    '</body></html>',
  ].join('\n')

  const editor = page.getByRole('textbox', { name: 'HTML' })
  await editor.click()
  await page.keyboard.press('ControlOrMeta+a')
  await page.keyboard.insertText(html)

  // Inline and <style> CSS apply; https: and data: images load, with no Referer.
  await expect(email.locator('.styled')).toHaveCSS('color', 'rgb(1, 2, 3)')
  await expect
    .poll(() =>
      email
        .getByRole('img', { name: 'Inline pixel' })
        .evaluate((image: HTMLImageElement) => image.naturalWidth),
    )
    .toBe(1)
  await expect(email.getByRole('img', { name: 'Remote image' })).toHaveAttribute(
    'referrerpolicy',
    'no-referrer',
  )
  await expect.poll(() => loads.filter((load) => load.url === remoteImage).length).toBe(1)
  // Chromium reports a request sent with no referrer as an empty header.
  expect(loads.find((load) => load.url === remoteImage)?.referer ?? '').toBe('')
  await expect(email.locator('meta[name="referrer"]')).toHaveCount(0)

  // A remote stylesheet is blocked; the script and the handler never run.
  await expect.poll(() => errors.some((error) => error.includes(remoteStyles))).toBe(true)
  await expect.poll(() => blocked).toContain(remoteStyles)
  await expect(email.locator('#target')).toHaveText('Untouched')
  await email.getByRole('button', { name: 'Press' }).click()
  await expect(email.getByRole('button', { name: 'Press' })).toHaveText('Press')

  // Only http, https, and mailto links keep their href, and they open a new page with no opener.
  await expect(email.getByText('Script link')).not.toHaveAttribute('href')
  await expect(email.getByText('Relative link')).not.toHaveAttribute('href')
  const opened = page.context().waitForEvent('page')
  await email.getByRole('link', { name: 'Safe link' }).click()
  const popup = await opened
  await popup.waitForLoadState()
  expect(popup.url()).toBe(`${baseURL}/favicon.svg`)
  expect(await popup.evaluate(() => window.opener as unknown)).toBeNull()
  await popup.close()
  const pages = page.context().pages().length
  await email.getByText('Script link').click()
  await page.waitForTimeout(500)
  expect(page.context().pages()).toHaveLength(pages)
  expect(await page.title()).not.toBe('Ran')

  // A subject only edit, and a 422, keep the same frame page: it is never reloaded.
  await page.getByLabel('Subject').fill('Only the subject, {{ project.name }}')
  await expect(page.getByTestId('preview-subject')).toHaveText('Only the subject, Scenarios')
  await page.getByLabel('Subject').fill('Broken {{ user.emial }}')
  await expect(page.getByText('The preview is waiting')).toBeVisible()
  await expect(email.locator('.styled')).toHaveText('Styled')
  expect(outerLoads).toHaveLength(1)

  // The only violations are the ones this HTML asked for: the stylesheet and the script.
  expect(errors.filter((error) => !error.includes(remoteStyles) && !/script/i.test(error))).toEqual(
    [],
  )
})
