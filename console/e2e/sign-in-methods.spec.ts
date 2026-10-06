import type { APIRequestContext } from '@playwright/test'

import { expect, scenariosProject, test } from './fixtures.ts'

// Spec 0012 AC-25 and AC-26 against the real API and its fake sign in provider, through the UI: the
// owner sees the four providers with their state, opens Google's settings (the secret only as its
// last 4, the callback URL), saves Microsoft's tenant, and then finds a user who signed in with
// GitHub on the Users page and in their identities.

const page = `/projects/${scenariosProject}/sign-in-methods`
const redirectUrl = 'http://localhost:3000/auth/callback'

/** A base64url string of a JSON value: the fake provider's test_user. */
function testUser(user: object): string {
  return Buffer.from(JSON.stringify(user)).toString('base64url')
}

/** Signs a user in with GitHub by redirect, as an app would, following the fake provider. */
async function signInWithGitHub(
  request: APIRequestContext,
  baseURL: string,
  sub: string,
  email: string,
): Promise<string> {
  const verifier = Buffer.from(crypto.getRandomValues(new Uint8Array(32))).toString('base64url')
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))
  const challenge = Buffer.from(digest).toString('base64url')
  const headers = { 'X-Orvano-Project': scenariosProject }
  const flow = await request.post(`${baseURL}/v1/account/oauth/flows`, {
    headers,
    data: { provider: 'github', redirectUrl, codeChallenge: challenge },
  })
  expect(flow.status()).toBe(200)
  let next = new URL(((await flow.json()) as { url: string }).url)
  next.searchParams.set(
    'test_user',
    testUser({ sub, email, emailVerified: true, name: 'E2E Octo' }),
  )
  for (let hop = 0; hop < 5; hop++) {
    // The provider URL names the api's own address; reach it through the gateway.
    const response = await request.get(`${baseURL}${next.pathname}${next.search}`, {
      maxRedirects: 0,
    })
    const location = response.headers().location as string | undefined
    if (location === undefined) throw new Error(`the flow stopped at ${String(response.status())}`)
    next = new URL(location, next)
    if (next.href.startsWith(redirectUrl)) break
  }
  const code = next.searchParams.get('orvano_code')
  const session = await request.post(`${baseURL}/v1/account/sessions/oauth`, {
    headers,
    data: { code, codeVerifier: verifier },
  })
  expect(session.status()).toBe(201)
  return ((await session.json()) as { user: { id: string } }).user.id
}

test('an owner reviews the providers, saves one, and finds a GitHub user and their identity', async ({
  signedIn: browser,
  request,
  baseURL,
}) => {
  if (baseURL === undefined) throw new Error('baseURL is not set')

  await browser.goto(page)
  await expect(browser).toHaveTitle('Sign in methods · Scenarios · Orvano')
  const providers = browser.getByRole('list', { name: 'Providers' })
  await expect(providers.getByRole('listitem')).toHaveCount(4)
  await expect(providers.getByRole('listitem').first()).toContainText('Ready: Redirect, Native')

  await browser.getByRole('button', { name: 'Edit Google' }).click()
  const dialog = browser.locator('[data-slot=dialog-content]')
  await expect(dialog.getByText('Set, ends in cret')).toBeVisible()
  await expect(dialog.getByLabel('Callback URL')).toHaveValue(
    new RegExp(`/v1/projects/${scenariosProject}/oauth/google/callback$`),
  )
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await browser.getByRole('button', { name: 'Edit Microsoft' }).click()
  await dialog.getByLabel('Tenant').fill('common')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(browser.getByText('Microsoft saved')).toBeVisible()

  const stamp = String(Date.now())
  const userId = await signInWithGitHub(request, baseURL, `9${stamp}`, `octo-${stamp}@example.com`)
  await browser.goto(`/projects/${scenariosProject}/users/${userId}`)
  const identities = browser.getByRole('region', { name: 'Identities' })
  await expect(identities.getByText(`octo-${stamp}@example.com`)).toBeVisible()
  await expect(identities.getByRole('button', { name: 'Unlink GitHub' })).toBeVisible()
})
