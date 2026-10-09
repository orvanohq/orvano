import { createHmac } from 'node:crypto'

import type { APIRequestContext } from '@playwright/test'

import { expect, scenariosProject, test } from './fixtures.ts'

// Spec 0013 AC-43 and AC-44 against the real API, through the UI: the owner sees the Authenticator
// app and Passkeys cards and turns TOTP off and on; then finds a user with MFA through the Users
// page's MFA filter, sees their session at two factors, and resets their MFA from the Security
// section, which ends every session.

// One test turns the project's authenticator app off for a moment, which would hide the other's MFA user from the
// MFA filter, so they run one after the other.
test.describe.configure({ mode: 'serial' })

const serverKey = 'orv_sk_scenarioScenarioScenarioScenarioScenario000'
const password = 'correct horse battery staple'

/** The 6 digit code an authenticator app shows for an unpadded base32 secret now (RFC 6238). */
function totpCode(secret: string): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const char of secret) bits += alphabet.indexOf(char).toString(2).padStart(5, '0')
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((byte) => Number.parseInt(byte, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const mac = createHmac('sha1', key).update(counter).digest()
  const offset = (mac.at(-1) ?? 0) & 0x0f
  const value = (mac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000
  return value.toString().padStart(6, '0')
}

/** A new user of the scenarios project with a verified email and a confirmed authenticator app. */
async function userWithMfa(request: APIRequestContext, baseURL: string, email: string) {
  const project = { 'X-Orvano-Project': scenariosProject }
  const created = await request.post(`${baseURL}/v1/users`, {
    headers: { ...project, 'X-Orvano-Key': serverKey },
    data: { email, password, emailVerified: true },
  })
  expect(created.status()).toBe(201)
  const signedIn = await request.post(`${baseURL}/v1/account/sessions/password`, {
    headers: project,
    data: { email, password },
  })
  expect(signedIn.status()).toBe(201)
  const token = ((await signedIn.json()) as { session: { accessToken: string } }).session
    .accessToken
  const bearer = { ...project, Authorization: `Bearer ${token}` }
  const setup = await request.post(`${baseURL}/v1/account/mfa/totp`, {
    headers: bearer,
    data: { password },
  })
  expect(setup.status()).toBe(201)
  const { secret } = (await setup.json()) as { secret: string }
  const confirmed = await request.post(`${baseURL}/v1/account/mfa/totp/confirm`, {
    headers: bearer,
    data: { code: totpCode(secret) },
  })
  expect(confirmed.status()).toBe(200)
}

test('an owner sees the MFA and passkey cards and turns the authenticator app off and on', async ({
  signedIn: page,
}) => {
  await page.goto(`/projects/${scenariosProject}/sign-in-methods`)
  const cards = page.getByRole('list', { name: 'MFA and passkeys' })
  await expect(cards.getByText('Authenticator app', { exact: true })).toBeVisible()
  await expect(cards.getByText('Passkeys', { exact: true })).toBeVisible()

  const totp = cards.getByRole('switch', { name: 'Enabled' })
  await expect(totp).toBeChecked()
  try {
    await totp.click()
    await expect(totp).not.toBeChecked()
    await expect(page.getByText('Authenticator app saved')).toBeVisible()
  } finally {
    // Other tests and scenarios expect TOTP on.
    if (!(await totp.isChecked())) await totp.click()
    await expect(totp).toBeChecked()
  }

  await cards.getByRole('button', { name: /passkeys$/ }).click()
  const dialog = page.getByRole('dialog', { name: 'Passkeys' })
  await expect(dialog.getByLabel('RP ID')).toBeVisible()
  await expect(dialog.getByRole('heading', { name: 'Accepted origins' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancel' }).click()
  await expect(dialog).toBeHidden()
})

test('an owner finds a user with MFA and resets it from the Security section', async ({
  signedIn: page,
  request,
  baseURL,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')
  const email = `e2e-mfa-${String(Date.now())}@example.com`
  await userWithMfa(request, baseURL, email)

  await page.goto(`/projects/${scenariosProject}/users?mfa=on`)
  await expect(page.getByLabel('MFA', { exact: true })).toContainText('On')
  await page.getByLabel('Search by email').fill(email)
  const row = page.getByRole('row').filter({ hasText: email })
  await expect(row.getByRole('cell', { name: 'On', exact: true })).toBeVisible()
  await row.getByRole('link', { name: email }).click()

  const security = page.getByRole('region', { name: 'Security' })
  await expect(security.getByText('On', { exact: true })).toBeVisible()
  await expect(security.getByText('No passkey yet.')).toBeVisible()
  const sessions = page.getByRole('region', { name: 'Active sessions' })
  await expect(sessions.getByRole('cell', { name: 'Two factors' })).toBeVisible()

  await security.getByRole('button', { name: 'Reset MFA' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Reset MFA' }).click()

  await expect(security.getByText('Off', { exact: true })).toBeVisible()
  await expect(page.getByText('No active sessions')).toBeVisible()
})
