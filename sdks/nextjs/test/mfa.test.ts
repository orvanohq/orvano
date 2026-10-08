import { NextRequest } from 'next/server.js'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountService, Client, MemorySessionStore } from '@orvano/js'
import {
  CookiePendingMfaStore,
  accessCookie,
  createServerClient,
  mfaCookie,
  mfaThroughHandler,
  refreshCookie,
} from '../src/index.js'
import type { CookieStore } from '../src/index.js'
import { createOrvanoRouteHandler } from '../src/server.js'

// Spec 0013 AC-37: a sign in that stops at the MFA step keeps its ticket in the HttpOnly
// orvano_mfa cookie, never in a body; the mfa, totp-confirm, and mfa-verify actions; the provider
// callback's redirect to mfaPath; and the browser transport that posts to them.

const endpoint = 'https://orvano.example.com'
const app = 'https://app.example.com'
const quiet = { warn: () => undefined }
const ticket = `orv_mt_${'B'.repeat(43)}`
const expiresAt = '2026-10-07T12:05:00Z'
const challenge = { ticket, factors: ['totp', 'recovery_code'], expiresAt }

function jwt(claims: Record<string, unknown>): string {
  const part = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${part({ alg: 'ES256', kid: 'k' })}.${part(claims)}.c2ln`
}

const session = {
  accessToken: jwt({ sub: 'u1', sid: 's1', exp: Math.floor(Date.now() / 1000) + 900 }),
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'orv_rt_new.secret',
  refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  sessionId: 's1',
}
const user = { id: 'u1', email: 'ada@example.com', emailVerified: true }
const challenged = (): Response =>
  Response.json(
    { user: null, session: null, mfa: challenge, isNewUser: false, verificationEmail: null },
    { status: 201 },
  )
const signedIn = (): Response =>
  Response.json(
    { user, session, mfa: null, isNewUser: false, verificationEmail: null },
    { status: 201 },
  )

interface Call {
  path: string
  headers: Headers
  body: Record<string, unknown>
}

/** A handler over a fake Orvano that answers by path and records each call. */
function handler(answers: Record<string, () => Response>, mfaPath?: string) {
  const calls: Call[] = []
  const fetch = (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const path = new URL(input instanceof Request ? input.url : input.toString()).pathname
    calls.push({
      path,
      headers: new Headers(init?.headers),
      body:
        typeof init?.body === 'string' ? (JSON.parse(init.body) as Record<string, unknown>) : {},
    })
    const answer = answers[path]
    return Promise.resolve(answer === undefined ? new Response(null, { status: 404 }) : answer())
  }
  return {
    ...createOrvanoRouteHandler({
      endpoint,
      project: 'shop',
      fetch,
      logger: quiet,
      ...(mfaPath === undefined ? {} : { mfaPath }),
    }),
    calls,
  }
}

function request(
  method: 'GET' | 'POST',
  path: string,
  body: unknown,
  cookies: Record<string, string>,
): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(`${app}${path}`, {
    method,
    headers: {
      origin: app,
      'content-type': 'application/json',
      ...(cookie === '' ? {} : { cookie }),
    },
    ...(method === 'POST' ? { body: JSON.stringify(body) } : {}),
  })
}

const post = (action: string, body: unknown, cookies: Record<string, string> = {}): NextRequest =>
  request('POST', `/api/orvano/${action}`, body, cookies)

/** The value a response set a cookie to, or undefined. */
function setCookie(response: Response, name: string): string | undefined {
  const cookie = response.headers.getSetCookie().find((c) => c.startsWith(`${name}=`))
  return cookie?.split(';')[0]?.slice(name.length + 1)
}

/** A cookie jar standing in for `await cookies()`. */
function jar(): CookieStore & { values: Map<string, string>; options: Map<string, unknown> } {
  const values = new Map<string, string>()
  const options = new Map<string, unknown>()
  return {
    values,
    options,
    get: (name) => {
      const value = values.get(name)
      return value === undefined ? undefined : { value }
    },
    set: (name, value, opts) => {
      values.set(name, value)
      options.set(name, opts)
    },
    delete: (name) => values.delete(name),
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('a server side sign in that stops at the MFA step (AC-37)', () => {
  it('keeps the ticket in the HttpOnly orvano_mfa cookie and returns it blank', async () => {
    const cookies = jar()
    const fetch = (): Promise<Response> => Promise.resolve(challenged())
    const orvano = createServerClient({ endpoint, project: 'shop', fetch, logger: quiet, cookies })

    const result = await orvano.account.createPasswordSession({
      email: 'ada@example.com',
      password: 'pw',
    })

    expect(result.mfa).toEqual({ ...challenge, ticket: '' })
    expect(cookies.values.get(accessCookie)).toBeUndefined()
    expect(cookies.options.get(mfaCookie)).toEqual({
      httpOnly: true,
      secure: true,
      sameSite: 'lax',
      path: '/',
      maxAge: 300,
    })
    const pending = new CookiePendingMfaStore(cookies).get()
    expect(pending?.ticket).toBe(ticket)
  })

  it('finishes on the server with completeMfa, setting the session and clearing orvano_mfa', async () => {
    const cookies = jar()
    let calls = 0
    const fetch = (): Promise<Response> =>
      Promise.resolve(calls++ === 0 ? challenged() : signedIn())
    const orvano = createServerClient({ endpoint, project: 'shop', fetch, logger: quiet, cookies })
    await new AccountService(orvano.client).createPasswordSession({
      email: 'ada@example.com',
      password: 'pw',
    })

    await orvano.client.completeMfa({ totpCode: '123456' })

    expect(cookies.values.get(mfaCookie)).toBeUndefined()
    expect(cookies.values.get(refreshCookie)).toBe('orv_rt_new.secret')
  })
})

describe('the route handler MFA actions (AC-37)', () => {
  it('email-code answers mfaRequired with the factors and sets orvano_mfa, never a ticket or session', async () => {
    const { POST } = handler({ '/v1/account/sessions/email-code': challenged })

    const response = await POST(
      post('email-code', { email: 'ada@example.com', code: '123456', next: '/account' }),
    )

    const body = (await response.json()) as Record<string, unknown>
    expect(body).toMatchObject({ mfaRequired: true, factors: ['totp', 'recovery_code'], expiresAt })
    expect(JSON.stringify(body)).not.toContain(ticket)
    expect(setCookie(response, accessCookie)).toBeUndefined()
    expect(setCookie(response, mfaCookie)).toBeTruthy()
    expect(response.headers.getSetCookie().find((c) => c.startsWith(mfaCookie))).toMatch(
      /HttpOnly/i,
    )
  })

  it('mfa reads the cookie, finishes the sign in, sets both cookies, clears orvano_mfa, and answers next', async () => {
    const first = handler({ '/v1/account/sessions/email-code': challenged })
    const started = await first.POST(
      post('email-code', { email: 'ada@example.com', code: '123456', next: '/account' }),
    )
    const { POST, calls } = handler({ '/v1/account/sessions/mfa': signedIn })

    const response = await POST(
      post(
        'mfa',
        { recoveryCode: 'AAAAA-BBBBB' },
        { [mfaCookie]: setCookie(started, mfaCookie) ?? '' },
      ),
    )

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ next: '/account' })
    expect(calls[0]?.body).toEqual({ ticket, recoveryCode: 'AAAAA-BBBBB' })
    expect(setCookie(response, refreshCookie)).toBe('orv_rt_new.secret')
    expect(setCookie(response, accessCookie)).toBe(session.accessToken)
    expect(setCookie(response, mfaCookie)).toBe('')
  })

  it('mfa without the cookie is 401 invalid_mfa_ticket, and a refused ticket clears it', async () => {
    const { POST, calls } = handler({
      '/v1/account/sessions/mfa': () =>
        Response.json({ status: 401, code: 'invalid_mfa_ticket' }, { status: 401 }),
    })

    const missing = await POST(post('mfa', { totpCode: '123456' }))
    expect(missing.status).toBe(401)
    expect(((await missing.json()) as { code: string }).code).toBe('invalid_mfa_ticket')
    expect(calls).toHaveLength(0)

    const first = handler({ '/v1/account/sessions/email-code': challenged })
    const started = await first.POST(post('email-code', { email: 'a@x.com', code: '123456' }))
    const ended = await POST(
      post('mfa', { totpCode: '123456' }, { [mfaCookie]: setCookie(started, mfaCookie) ?? '' }),
    )
    expect(ended.status).toBe(401)
    expect(setCookie(ended, mfaCookie)).toBe('')
  })

  it('mfa refuses a body with no factor or two', async () => {
    const { POST } = handler({})
    expect((await POST(post('mfa', {}))).status).toBe(400)
    expect((await POST(post('mfa', { totpCode: '1', recoveryCode: '2' }))).status).toBe(400)
  })

  it('totp-confirm turns MFA on as the user and sets the new access cookie', async () => {
    const { POST, calls } = handler({
      '/v1/account/mfa/totp/confirm': () =>
        Response.json({ recoveryCodes: ['AAAAA-BBBBB'], session }),
    })

    const response = await POST(
      post('totp-confirm', { code: '123456' }, { [accessCookie]: session.accessToken }),
    )

    expect(await response.json()).toEqual({ recoveryCodes: ['AAAAA-BBBBB'] })
    expect(calls[0]?.headers.get('Authorization')).toBe(`Bearer ${session.accessToken}`)
    expect(setCookie(response, accessCookie)).toBe(session.accessToken)
  })

  it('mfa-verify steps up as the user and answers 204', async () => {
    const { POST, calls } = handler({ '/v1/account/mfa/verify': () => Response.json(session) })

    const response = await POST(
      post('mfa-verify', { totpCode: '123456' }, { [accessCookie]: session.accessToken }),
    )

    expect(response.status).toBe(204)
    expect(calls[0]?.body).toEqual({ totpCode: '123456' })
    expect(setCookie(response, accessCookie)).toBe(session.accessToken)
  })

  it('mfa-verify with nobody signed in is 401 before calling Orvano', async () => {
    const { POST, calls } = handler({})
    expect((await POST(post('mfa-verify', { totpCode: '123456' }))).status).toBe(401)
    expect(calls).toHaveLength(0)
  })

  it('the provider callback sends a challenged sign in to mfaPath and keeps next in orvano_mfa', async () => {
    const start = handler({
      '/v1/account/oauth/flows': () => Response.json({ url: 'https://accounts.example/auth' }),
    })
    const started = await start.POST(post('oauth', { provider: 'google', next: '/welcome' }))
    const { GET } = handler({ '/v1/account/sessions/oauth': challenged }, '/auth/two-step')

    const response = await GET(
      request(
        'GET',
        '/api/orvano/oauth-callback?orvano_type=oauth&orvano_code=orv_oc_x',
        undefined,
        { orvano_oauth: setCookie(started, 'orvano_oauth') ?? '' },
      ),
    )

    expect(response.status).toBe(303)
    expect(response.headers.get('location')).toBe(`${app}/auth/two-step`)
    expect(setCookie(response, accessCookie)).toBeUndefined()
    const done = handler({ '/v1/account/sessions/mfa': signedIn })
    const finished = await done.POST(
      post('mfa', { totpCode: '123456' }, { [mfaCookie]: setCookie(response, mfaCookie) ?? '' }),
    )
    expect(await finished.json()).toEqual({ next: '/welcome' })
  })
})

describe('the browser MFA transport (AC-37)', () => {
  it('posts to the handler and reloads the session from the cookies', async () => {
    const fetch = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/totp-confirm')
          ? Response.json({ recoveryCodes: ['AAAAA-BBBBB'] })
          : url.endsWith('/mfa-verify')
            ? new Response(null, { status: 204 })
            : Response.json({ next: '/' }),
      ),
    )
    vi.stubGlobal('fetch', fetch)
    const client = new Client({
      endpoint,
      session: new MemorySessionStore(),
      mfa: mfaThroughHandler('/api/orvano'),
      logger: quiet,
    })
    const events: string[] = []
    client.onAuthStateChange((event) => events.push(event))

    await client.completeMfa({ totpCode: '123456' })
    await client.verifyMfa({ recoveryCode: 'AAAAA-BBBBB' })
    expect(await client.confirmTotp('123456')).toEqual(['AAAAA-BBBBB'])

    expect(fetch.mock.calls.map((c) => c[0])).toEqual([
      '/api/orvano/mfa',
      '/api/orvano/mfa-verify',
      '/api/orvano/totp-confirm',
    ])
    expect(events).toEqual(['signedIn', 'tokenRefreshed', 'tokenRefreshed'])
  })
})

const passkeyChallenge = { challengeId: 'ch1', options: { challenge: 'Y2g', rpId: 'example.com' } }
const credential = {
  id: 'cred',
  rawId: 'cred',
  type: 'public-key',
  response: { clientDataJSON: 'cd', authenticatorData: 'ad', signature: 'sig' },
}

describe('the route handler passkey actions (AC-37)', () => {
  it('mfa-passkey answers the challenge for the cookie ticket, never the ticket itself', async () => {
    const first = handler({ '/v1/account/sessions/email-code': challenged })
    const started = await first.POST(post('email-code', { email: 'a@x.com', code: '123456' }))
    const { POST, calls } = handler({
      '/v1/account/sessions/mfa/passkey-challenge': () => Response.json(passkeyChallenge),
    })

    const response = await POST(
      post('mfa-passkey', {}, { [mfaCookie]: setCookie(started, mfaCookie) ?? '' }),
    )

    expect(await response.json()).toEqual(passkeyChallenge)
    expect(calls[0]?.body).toEqual({ ticket })
    expect((await POST(post('mfa-passkey', {}))).status).toBe(401)
  })

  it('mfa takes a passkey answer flat and sends it nested', async () => {
    const first = handler({ '/v1/account/sessions/email-code': challenged })
    const started = await first.POST(post('email-code', { email: 'a@x.com', code: '123456' }))
    const { POST, calls } = handler({ '/v1/account/sessions/mfa': signedIn })

    const response = await POST(
      post(
        'mfa',
        { challengeId: 'ch1', credential },
        { [mfaCookie]: setCookie(started, mfaCookie) ?? '' },
      ),
    )

    expect(response.status).toBe(200)
    expect(calls[0]?.body).toEqual({ ticket, passkey: { challengeId: 'ch1', credential } })
    expect(setCookie(response, refreshCookie)).toBe('orv_rt_new.secret')
  })

  it('passkey-challenge and passkey sign in with no typing and set both cookies', async () => {
    const { POST, calls } = handler({
      '/v1/account/sessions/passkey-challenge': () => Response.json(passkeyChallenge),
      '/v1/account/sessions/passkey': signedIn,
    })

    const started = await POST(post('passkey-challenge', {}))
    expect(await started.json()).toEqual(passkeyChallenge)
    const response = await POST(post('passkey', { challengeId: 'ch1', credential }))

    expect(await response.json()).toEqual({ user, isNewUser: false })
    expect(calls[1]?.body).toEqual({ challengeId: 'ch1', credential })
    expect(setCookie(response, accessCookie)).toBe(session.accessToken)
    expect(setCookie(response, refreshCookie)).toBe('orv_rt_new.secret')
    expect((await POST(post('passkey', { challengeId: 'ch1' }))).status).toBe(400)
  })

  it('mfa-verify passes a nested passkey answer through', async () => {
    const { POST, calls } = handler({ '/v1/account/mfa/verify': () => Response.json(session) })

    const response = await POST(
      post(
        'mfa-verify',
        { passkey: { challengeId: 'ch1', credential } },
        { [accessCookie]: session.accessToken },
      ),
    )

    expect(response.status).toBe(204)
    expect(calls[0]?.body).toEqual({ passkey: { challengeId: 'ch1', credential } })
  })

  it('the browser transport runs the ceremony in the browser and posts to the actions', async () => {
    const fetch = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/passkey')
          ? Response.json({ user, isNewUser: false })
          : url.endsWith('/mfa')
            ? Response.json({ next: '/' })
            : Response.json(passkeyChallenge),
      ),
    )
    vi.stubGlobal('fetch', fetch)
    const client = new Client({
      endpoint,
      project: 'shop',
      session: new MemorySessionStore(),
      mfa: mfaThroughHandler('/api/orvano'),
      passkeys: {
        isSupported: () => Promise.resolve(true),
        create: () => Promise.reject(new Error('not used')),
        get: () => Promise.resolve(credential as never),
      },
      logger: quiet,
    })

    const outcome = await client.signInWithPasskey()
    await client.completeMfa({ passkey: true })

    expect(outcome.user).toEqual(user)
    expect(fetch.mock.calls.map((c) => c[0])).toEqual([
      '/api/orvano/passkey-challenge',
      '/api/orvano/passkey',
      '/api/orvano/mfa-passkey',
      '/api/orvano/mfa',
    ])
    expect(
      JSON.parse((fetch.mock.calls[3] as unknown as [string, { body: string }])[1].body),
    ).toEqual({
      challengeId: 'ch1',
      credential,
    })
  })
})
