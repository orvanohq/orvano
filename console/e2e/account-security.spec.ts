import { createHmac, randomUUID } from 'node:crypto'

import type { APIRequestContext, Page } from '@playwright/test'

import { cookieHeader, expect, fixturesOrgId, test, type SessionCookie } from './fixtures.ts'

// Spec 0013 AC-41, AC-42, and AC-45 against the real API, through the UI: a new console account
// turns on its authenticator app from the Security page and signs in again through the Two step
// verification step; another adds a passkey with Playwright's virtual authenticator and signs in
// with it. Each test uses its own account, because turning MFA on ends the account's other
// sessions, and the fixture admin's session is shared by every test.
//
// Console passkeys accept only the api's public origin (AC-3), so the passkey test runs only when
// the stack was started with ORVANO_SCENARIOS_PUBLIC_URL set to this console's URL, as CI does:
//   ORVANO_SCENARIOS_PUBLIC_URL=http://localhost:8081 docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait

const password = 'security horse battery'

/** The code an authenticator app shows for an unpadded base32 secret, `steps` steps from now. */
function totpCode(secret: string, steps = 0): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const char of secret) bits += alphabet.indexOf(char).toString(2).padStart(5, '0')
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((byte) => Number.parseInt(byte, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000) + steps))
  const mac = createHmac('sha1', key).update(counter).digest()
  const offset = (mac.at(-1) ?? 0) & 0x0f
  const value = (mac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000
  return value.toString().padStart(6, '0')
}

/** A platform authenticator that holds discoverable passkeys and verifies the user at once. */
async function addVirtualAuthenticator(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('WebAuthn.enable')
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  })
}

async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/\/sign-in/)
}

/** A new console account (a viewer of the Fixtures org), signed in inside the page's own context. */
async function newAccount(
  page: Page,
  request: APIRequestContext,
  baseURL: string,
  session: SessionCookie[],
): Promise<string> {
  const email = `security-${randomUUID()}@example.com`
  const orgId = await fixturesOrgId(request, baseURL, session)
  const invited = await request.post(`${baseURL}/v1/console/orgs/${orgId}/invitations`, {
    headers: { cookie: cookieHeader(session), 'sec-fetch-site': 'same-origin' },
    data: { email, role: 'viewer' },
  })
  expect(invited.status()).toBe(201)
  const inviteToken = new URL(((await invited.json()) as { url: string }).url).hash.slice(1)
  const created = await page.request.post(`${baseURL}/v1/console/account`, {
    headers: { 'sec-fetch-site': 'same-origin' },
    data: { email, password, inviteToken },
  })
  expect(created.status()).toBe(201)
  return email
}

test('a console account turns on its authenticator app and then signs in with a code', async ({
  page,
  request,
  baseURL,
  session,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  const email = await newAccount(page, request, baseURL, session)

  await page.goto('/account/security')
  await page.getByRole('button', { name: 'Turn on' }).click()
  // Adding a way to sign in needs the password, even right after signing in.
  await page.getByRole('dialog').getByLabel('Password').fill(password)
  await page.getByRole('dialog').getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('img', { name: 'QR code for your authenticator app' })).toBeVisible()
  const secret = (await page.locator('code').first().textContent()) ?? ''
  expect(secret).toMatch(/^[A-Z2-7]{32}$/)
  await page.getByLabel('Authentication code').fill(totpCode(secret))
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(
    page.getByRole('list', { name: 'Recovery codes' }).getByRole('listitem'),
  ).toHaveCount(10)
  await page.getByRole('button', { name: 'I saved them' }).click()
  await expect(page.getByText('10 of 10')).toBeVisible()

  // The code of the step after the one the confirm used: each step's code works once.
  await signOut(page)
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Two step verification' })).toBeVisible()
  await page.getByLabel('Authentication code').fill(totpCode(secret, 1))
  await page.getByRole('button', { name: 'Verify' }).click()
  await expect(page).not.toHaveURL(/\/sign-in/)
})

test('a console account adds a passkey and signs in with it', async ({
  page,
  request,
  baseURL,
  session,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  test.skip(
    process.env.ORVANO_SCENARIOS_PUBLIC_URL !== baseURL,
    'Console passkeys need the api public URL to be this console: set ORVANO_SCENARIOS_PUBLIC_URL',
  )
  await newAccount(page, request, baseURL, session)
  await addVirtualAuthenticator(page)

  await page.goto('/account/security')
  await page.getByRole('button', { name: 'Add a passkey' }).click()
  await page.getByRole('dialog').getByLabel('Password').fill(password)
  await page.getByRole('dialog').getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('cell', { name: 'Device bound' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Never' })).toBeVisible()

  await signOut(page)
  await page.getByRole('button', { name: 'Sign in with a passkey' }).click()
  await expect(page).not.toHaveURL(/\/sign-in/)
  await page.goto('/account/security')
  await expect(page.getByRole('cell', { name: 'Device bound' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Never' })).toHaveCount(0)
})
