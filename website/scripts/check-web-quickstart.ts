/**
 * The CI check for the web app quickstarts (spec 0011, AC-11, AC-19): in Chromium, sign up, see your name and email,
 * sign out, sign in again, and sign out, in a running Next.js or JavaScript example.
 *
 *   pnpm --filter @orvano/website check:web-quickstart http://localhost:3000
 */
import { randomUUID } from 'node:crypto'
import { chromium, type Page } from 'playwright'

const appUrl = process.argv[2]
if (appUrl === undefined)
  throw new Error('Pass the running app URL, for example http://localhost:3000.')

const email = `quickstart-${randomUUID()}@example.com`
const password = randomUUID()
const name = 'Grace Hopper'

const browser = await chromium.launch()
try {
  const page = await browser.newPage()
  page.setDefaultTimeout(20_000)
  await page.goto(appUrl)

  const signUp = page.getByRole('form', { name: 'Sign up' })
  await signUp.getByLabel('Name').fill(name)
  await signUp.getByLabel('Email').fill(email)
  await signUp.getByLabel('Password').fill(password)
  await signUp.getByRole('button', { name: 'Sign up' }).click()
  await expectSignedIn(page)

  await page.getByRole('button', { name: 'Sign out' }).click()
  const signIn = page.getByRole('form', { name: 'Sign in' })
  await signIn.getByLabel('Email').fill(email)
  await signIn.getByLabel('Password').fill(password)
  await signIn.getByRole('button', { name: 'Sign in' }).click()
  await expectSignedIn(page)

  // Still signed in after a reload: the session outlives the page.
  await page.reload()
  await expectSignedIn(page)

  await page.getByRole('button', { name: 'Sign out' }).click()
  await page.getByRole('form', { name: 'Sign in' }).waitFor()
  console.log(`${appUrl}: signed up, signed in, and signed out.`)
} finally {
  await browser.close()
}

async function expectSignedIn(page: Page): Promise<void> {
  await page.getByRole('heading', { name: "You're signed in" }).waitFor()
  await page.getByText(`Name: ${name}`).waitFor()
  await page.getByText(`Email: ${email}`).waitFor()
}
