/**
 * The quickstarts' console journey (spec 0011, AC-10, AC-19, AC-21), run against a fresh local stack: it creates
 * the first admin from the setup link, a project in the admin's personal org, the platforms the quickstarts need,
 * and a server API key, through the console like a reader would. It saves each step's screenshot in light and dark
 * to `src/assets/screenshots/`, and the IDs and key it created to `quickstart.env`.
 *
 *   ORVANO_LOCAL_DIR=<folder of an `install --local`> pnpm --filter @orvano/website screenshots
 *
 * `QUICKSTART_ENV` changes where the IDs go; `SCREENSHOTS=0` skips the images (CI only needs the IDs).
 */
import { randomUUID } from 'node:crypto'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { join, resolve } from 'node:path'
import { chromium, type Locator, type Page } from 'playwright'

/** The iOS bundle ID and Android package name; the Flutter quickstart uses the same constant (AC-10). */
const appId = 'dev.orvano.quickstart'

const localDir = process.env.ORVANO_LOCAL_DIR ?? ''
if (localDir === '')
  throw new Error('Set ORVANO_LOCAL_DIR to the folder you ran `install --local` in.')

const env = parseEnv(await readFile(join(localDir, '.env'), 'utf8'))
const consoleUrl = required(env, 'ORVANO_PUBLIC_URL')
const setupToken = required(env, 'ORVANO_SETUP_TOKEN')
const outFile = resolve(process.env.QUICKSTART_ENV ?? 'quickstart.env')
const takeShots = process.env.SCREENSHOTS !== '0'
const shotDir = resolve('src/assets/screenshots')

const browser = await chromium.launch()
try {
  const page = await browser.newPage({
    viewport: { width: 1280, height: 800 },
    colorScheme: 'dark',
  })
  page.setDefaultTimeout(30_000)

  // The first admin, from the setup link the installer printed.
  await page.goto(`${consoleUrl}/setup#${setupToken}`)
  // The page removes the token from the address and loads the route again, which can remount the form; fill it only
  // once that has settled, or the typed values are lost and the click submits an empty form.
  await page.waitForURL((url) => url.hash === '')
  await page.waitForLoadState('networkidle')
  await page.getByLabel('Name').fill('Ada Lovelace')
  await page.getByLabel('Email').fill('ada@example.com')
  await page.getByLabel('Password').fill(randomUUID())
  await shot(page, 'setup', page.locator('main'))
  await page.getByRole('button', { name: 'Create the first admin' }).click()
  await page.waitForURL(/\/orgs\/[^/]+$/)
  const orgUrl = page.url()

  // A project in the personal org the admin starts with.
  await page.getByRole('button', { name: 'Create project' }).first().click()
  const projectDialog = page.getByRole('dialog', { name: 'Create a project' })
  await projectDialog.getByLabel('Name', { exact: true }).fill('My app')
  await shot(page, 'create-project', projectDialog)
  await projectDialog.getByRole('button', { name: 'Create project' }).click()
  await page.waitForURL(/\/projects\/[a-z0-9]{20}$/)
  const projectId = /\/projects\/([a-z0-9]{20})$/.exec(page.url())?.[1] ?? ''
  await page.getByRole('heading', { level: 1, name: 'My app' }).waitFor()
  await page.getByText('Project ID').waitFor()
  await shot(page, 'project-overview', page.locator('main dl').first())

  // One Web platform covers every port on localhost; iOS and Android share one app ID.
  const nav = page.getByRole('navigation', { name: 'Project navigation' })
  await nav.getByRole('link', { name: 'Platforms' }).click()
  await page.getByRole('heading', { level: 1, name: 'Platforms' }).waitFor()
  await addPlatform(page, 'Web', 'Local web', 'Hostname', 'localhost', 'add-web-platform')
  await addPlatform(page, 'iOS', 'Quickstart iOS', 'Bundle ID', appId, 'add-ios-platform')
  await addPlatform(
    page,
    'Android',
    'Quickstart Android',
    'Package name',
    appId,
    'add-android-platform',
  )

  // A server key for the Dart and .NET quickstarts.
  await nav.getByRole('link', { name: 'API keys' }).click()
  await page.getByRole('heading', { level: 1, name: 'API keys' }).waitFor()
  await page.getByRole('button', { name: 'Create key' }).first().click()
  const keyDialog = page.getByRole('dialog', { name: 'Create an API key' })
  await keyDialog.getByLabel('Name', { exact: true }).fill('Quickstart server')
  await keyDialog.getByRole('checkbox', { name: 'Users read' }).click()
  await keyDialog.getByRole('checkbox', { name: 'Users write' }).click()
  await shot(page, 'create-api-key', keyDialog)
  await keyDialog.getByRole('button', { name: 'Create key' }).click()
  const reveal = page.getByRole('dialog', { name: 'Copy your API key' })
  const apiKey = ((await reveal.getByLabel('API key', { exact: true }).textContent()) ?? '').trim()
  if (!apiKey.startsWith('orv_sk_')) throw new Error('The console showed no API key.')
  await reveal.getByRole('checkbox', { name: "I've copied this key and stored it safely" }).click()
  await reveal.getByRole('button', { name: 'Done' }).click()
  await reveal.waitFor({ state: 'hidden' })

  await writeFile(
    outFile,
    [
      '# Written by `pnpm --filter @orvano/website screenshots` (spec 0011). Never commit it.',
      `ORVANO_ENDPOINT=${consoleUrl}`,
      `ORVANO_PROJECT=${projectId}`,
      `ORVANO_API_KEY=${apiKey}`,
      '',
    ].join('\n'),
    { mode: 0o600 },
  )
  console.log(`Project ${projectId} is ready; its IDs and key are in ${outFile}.`)

  // The console guides' screenshots: email, team members, and the install's email server. CI skips them.
  if (takeShots) await consoleGuideShots(page, nav, orgUrl)
} finally {
  await browser.close()
}

/** The screens the console guide pages show, after the quickstart journey (AC-21). */
async function consoleGuideShots(page: Page, nav: Locator, orgUrl: string): Promise<void> {
  // A reload clears the earlier steps' toasts, which would cover these pages.
  await page.reload()

  // The project's email pages. A local stack's install email server (Mailpit) sends for the project.
  await nav.getByRole('link', { name: 'Email' }).click()
  const emailTabs = page.getByRole('navigation', { name: 'Email sections' })
  await emailTabs.getByRole('link', { name: 'Settings' }).click()
  await page.getByText('Using this server’s email settings').waitFor()
  await shot(page, 'email-settings', page.locator('main'))
  await emailTabs.getByRole('link', { name: 'Templates' }).click()
  const templates = page.getByRole('region', { name: 'Templates' })
  await templates.getByRole('link', { name: 'Email verification' }).waitFor()
  await shot(page, 'email-templates', templates)
  await templates.getByRole('link', { name: 'Email verification' }).click()
  await page.getByRole('button', { name: 'Save' }).waitFor()
  // The preview renders shortly after the editor loads.
  await page.waitForTimeout(1500)
  await shot(page, 'email-template-editor', page.locator('main'))

  // Inviting a teammate from the org's Members page; closed before it creates the invite.
  await page.goto(orgUrl)
  await page
    .getByRole('navigation', { name: 'Org navigation' })
    .getByRole('link', { name: 'Members' })
    .click()
  await page.getByRole('heading', { level: 1, name: 'Members' }).waitFor()
  await page.getByRole('button', { name: 'Invite', exact: true }).click()
  const invite = page.getByRole('dialog', { name: 'Invite a teammate' })
  await invite.getByLabel('Email').fill('grace@example.com')
  await shot(page, 'invite-member', invite)
  await page.keyboard.press('Escape')
  await invite.waitFor({ state: 'hidden' })

  // The install's email server, which `install --local` points at Mailpit.
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Install settings' }).click()
  const server = page
    .locator('[data-slot="card"]')
    .filter({ has: page.getByRole('heading', { name: 'Email server' }) })
  await server.getByLabel('Host', { exact: true }).waitFor()
  await shot(page, 'install-email-server', server)
}

async function addPlatform(
  page: Page,
  type: string,
  name: string,
  identifierLabel: string,
  identifier: string,
  shotName: string,
): Promise<void> {
  await page.getByRole('button', { name: 'Add platform' }).first().click()
  const dialog: Locator = page.getByRole('dialog', { name: 'Add a platform' })
  if (type !== 'Web') {
    await dialog.getByRole('combobox', { name: 'Type' }).click()
    await page.getByRole('option', { name: type }).click()
  }
  await dialog.getByLabel('Name', { exact: true }).fill(name)
  await dialog.getByLabel(identifierLabel).fill(identifier)
  await shot(page, shotName, dialog)
  await dialog.getByRole('button', { name: 'Add platform' }).click()
  await dialog.waitFor({ state: 'hidden' })
  await page.getByRole('cell', { name: identifier, exact: true }).first().waitFor()
}

/**
 * Saves `<name>-light.png` and `<name>-dark.png`, switching the console's theme in place. With `target`, the image is
 * cropped to it plus a margin, so a dialog stays readable at the docs' column width.
 */
async function shot(page: Page, name: string, target?: Locator): Promise<void> {
  if (!takeShots) return
  await mkdir(shotDir, { recursive: true })
  const margin = 24
  const box = target === undefined ? null : await target.boundingBox()
  const viewport = page.viewportSize() ?? { width: 1280, height: 800 }
  const clip =
    box === null
      ? undefined
      : {
          x: Math.max(0, box.x - margin),
          y: Math.max(0, box.y - margin),
          width: Math.min(viewport.width, box.x + box.width + margin) - Math.max(0, box.x - margin),
          height:
            Math.min(viewport.height, box.y + box.height + margin) - Math.max(0, box.y - margin),
        }
  for (const theme of ['light', 'dark'] as const) {
    await page.evaluate((value) => {
      document.documentElement.dataset.theme = value
    }, theme)
    // Let transitions settle so both images show the same state.
    await page.waitForTimeout(300)
    await page.screenshot({
      path: join(shotDir, `${name}-${theme}.png`),
      ...(clip === undefined ? {} : { clip }),
    })
  }
}

function parseEnv(text: string): Map<string, string> {
  const values = new Map<string, string>()
  for (const line of text.split('\n')) {
    const match = /^([A-Z0-9_]+)=(.*)$/.exec(line.trim())
    if (match?.[1] !== undefined)
      values.set(match[1], (match[2] ?? '').replace(/^(['"])(.*)\1$/, '$2'))
  }
  return values
}

function required(values: Map<string, string>, key: string): string {
  const value = values.get(key)
  if (value === undefined || value === '')
    throw new Error(`${key} is missing from ${localDir}/.env.`)
  return value
}
