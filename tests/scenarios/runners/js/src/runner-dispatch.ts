import { createNonce, createPkce } from '@orvano/js'
import type { Client as ServerClient } from '@orvano/js/server'
import type { DispatchTable } from './dispatch-table.js'

/** The fake provider's headers for Apple's form post (spec 0012, AC-27): its target and urlencoded body. */
const formActionHeader = 'x-orvano-test-form-action'
const formBodyHeader = 'x-orvano-test-form-body'

/** In a browser the runner's page proxies `/v1`, and turns a redirect into this header (a browser can't read one). */
const proxiedLocationHeader = 'x-orvano-test-location'

const inBrowser = typeof document !== 'undefined'

/**
 * Follows the fake provider the way a browser would, over HTTP (spec 0012, AC-24): adds `test_user`,
 * follows redirects and Apple's form post, and returns the URL that leaves for `redirectUrl`.
 */
export async function followOAuth(
  url: string,
  testUser: unknown,
  redirectUrl: string,
): Promise<string> {
  let next = new URL(url)
  next.searchParams.set('test_user', base64Url(JSON.stringify(testUser)))
  let init: RequestInit = { method: 'GET' }
  for (let hop = 0; hop < 10; hop++) {
    // In a browser everything goes through the page's same origin proxy.
    const target = inBrowser ? next.pathname + next.search : next.href
    const headers = new Headers(init.headers)
    headers.set('x-orvano-test-manual-redirect', '1')
    const response = await fetch(target, { ...init, headers, redirect: 'manual' })
    const location = response.headers.get('location') ?? response.headers.get(proxiedLocationHeader)
    if (location !== null) {
      const to = new URL(location, next)
      if (to.href.startsWith(redirectUrl)) return to.href
      next = to
      init = { method: 'GET' }
      continue
    }
    const action = response.headers.get(formActionHeader)
    const body = response.headers.get(formBodyHeader)
    if (action !== null && body !== null) {
      next = new URL(action, next)
      init = {
        method: 'POST',
        body,
        headers: { 'content-type': 'application/x-www-form-urlencoded' },
      }
      continue
    }
    throw new Error(
      `the provider flow stopped at ${String(response.status)}: ${await response.text()}`,
    )
  }
  throw new Error('the provider flow redirected more than 10 times')
}

function base64Url(text: string): string {
  let binary = ''
  for (const b of new TextEncoder().encode(text)) binary += String.fromCharCode(b)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const defaultRedirect = 'http://localhost:3000/auth/callback'

/**
 * Runner operations: calls the scenarios make that are not contract operations. `signIn` is a
 * plain sign in call that leaves the SDK's stored session alone, so a runner without client
 * operations (.NET) can get a token too; `verifyAccessToken` is the server SDK's own check; `now`
 * is the runner's clock, saved before a send and passed to `test.getLatestEmail` as `after`;
 * `redeemLink` is the client SDK's link helper (spec 0010); `oauthSignIn` runs the client SDK's
 * `signInWithOAuth` or `linkIdentity` with an `open` that follows the fake provider over HTTP,
 * `oauthCode` stops at the code so a scenario can redeem it itself, and `createNonce` is the SDK's
 * native nonce (spec 0012). Their names have no dot, so they never collide with an operationId.
 */
export const runnerDispatch: DispatchTable = {
  now: {
    status: 200,
    client: () => Promise.resolve({ now: new Date().toISOString() }),
    server: () => Promise.resolve({ now: new Date().toISOString() }),
  },
  redeemLink: {
    status: 200,
    client: async (o, input) => {
      const password = typeof input.password === 'string' ? { password: input.password } : {}
      return (await o.client.redeemLink(String(input.url), password)) ?? null
    },
  },
  createNonce: {
    status: 200,
    client: async () => createNonce(),
  },
  oauthSignIn: {
    status: 200,
    client: async (o, input) => {
      const redirectUrl =
        typeof input.redirectUrl === 'string' ? input.redirectUrl : defaultRedirect
      const options = {
        redirectUrl,
        open: (url: string) => followOAuth(url, input.testUser, redirectUrl),
      }
      const provider = String(input.provider) as 'google' | 'apple' | 'github' | 'microsoft'
      return input.link === true
        ? await o.client.linkIdentity(provider, options)
        : await o.client.signInWithOAuth(provider, options)
    },
  },
  oauthCode: {
    status: 200,
    client: async (o, input) => {
      const redirectUrl =
        typeof input.redirectUrl === 'string' ? input.redirectUrl : defaultRedirect
      const { verifier, challenge } = await createPkce()
      const flow = await o.client.request<{ url: string }>({
        method: 'POST',
        path: '/v1/account/oauth/flows',
        body: { provider: input.provider, redirectUrl, codeChallenge: challenge },
      })
      const back = new URL(await followOAuth(flow.url, input.testUser, redirectUrl))
      return {
        type: back.searchParams.get('orvano_type'),
        code: back.searchParams.get('orvano_code'),
        error: back.searchParams.get('orvano_error'),
        codeVerifier: verifier,
      }
    },
  },
  signIn: {
    status: 201,
    client: (o, input) =>
      o.client.request<unknown>({
        method: 'POST',
        path: '/v1/account/sessions/password',
        body: input.body,
      }),
  },
  verifyAccessToken: {
    status: 200,
    server: async (o, input) => {
      const verified = await (o.client as ServerClient).verifyAccessToken(String(input.token), {
        online: input.online === true,
      })
      return {
        userId: verified.userId,
        sessionId: verified.sessionId,
        emailVerified: verified.emailVerified,
        expiresAt: verified.expiresAt.toISOString(),
      }
    },
  },
}
