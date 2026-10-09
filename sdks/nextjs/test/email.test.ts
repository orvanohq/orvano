import { NextRequest } from 'next/server.js'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { Client, MemorySessionStore } from '@orvano/js'
import { accessCookie, emailAuthThroughHandler, refreshCookie } from '../src/index.js'
import { createOrvanoRouteHandler } from '../src/server.js'

// Spec 0010 AC-24 and AC-25: the route handler's redeem and email-code actions, and the browser
// transport that posts to them.

const endpoint = 'https://orvano.example.com'
const app = 'https://app.example.com'
const quiet = { warn: () => undefined }
const token = `orv_el_${'A'.repeat(43)}`

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

interface Call {
  path: string
  headers: Headers
  body: unknown
}

/** A fake Orvano answering in order (the last repeats), recording each call. */
function orvano(...answers: (() => Response)[]): { fetch: typeof fetch; calls: Call[] } {
  const calls: Call[] = []
  const fetch = (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    calls.push({
      path: new URL(input instanceof Request ? input.url : input.toString()).pathname,
      headers: new Headers(init?.headers),
      body: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined,
    })
    const answer = answers[Math.min(calls.length - 1, answers.length - 1)]
    if (answer === undefined) throw new Error('no answer')
    return Promise.resolve(answer())
  }
  return { fetch, calls }
}

function handler(...answers: (() => Response)[]) {
  const fake = orvano(...answers)
  return {
    ...fake,
    POST: createOrvanoRouteHandler({ endpoint, project: 'shop', fetch: fake.fetch, logger: quiet })
      .POST,
  }
}

function post(
  action: string,
  body: unknown,
  cookies: Record<string, string> = {},
  origin = app,
): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(`${app}/api/orvano/${action}`, {
    method: 'POST',
    headers: {
      origin,
      'content-type': 'application/json',
      'x-forwarded-for': '6.6.6.6, 203.0.113.7',
      'user-agent': 'Browser/1.0',
      ...(cookie === '' ? {} : { cookie }),
    },
    body: JSON.stringify(body),
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('the redeem action (AC-25)', () => {
  it('signs in by magic link as the browser and sets both cookies, answering no token', async () => {
    const { POST, calls } = handler(() =>
      Response.json({ user, session, isNewUser: true }, { status: 201 }),
    )

    const response = await POST(post('redeem', { type: 'magic_link', token }))
    const body = (await response.json()) as Record<string, unknown>

    expect(response.status).toBe(200)
    expect(body).toEqual({
      type: 'magic_link',
      user,
      isNewUser: true,
      mfaRequired: false,
      factors: [],
    })
    expect(JSON.stringify(body)).not.toContain('orv_rt_')
    expect(calls[0]?.path).toBe('/v1/account/sessions/magic-link')
    expect(calls[0]?.headers.get('X-Orvano-Client-IP')).toBe('203.0.113.7')
    expect(calls[0]?.headers.get('X-Orvano-Client-UA')).toBe('Browser/1.0')
    const cookies = response.headers.getSetCookie()
    expect(cookies.some((c) => c.startsWith(`${accessCookie}=`))).toBe(true)
    expect(
      cookies.some(
        (c) => c.startsWith(`${refreshCookie}=orv_rt_new.secret`) && c.includes('HttpOnly'),
      ),
    ).toBe(true)
  })

  it('verifies and then refreshes the cookies that exist, so the claim is current', async () => {
    const { POST, calls } = handler(
      () => Response.json(user),
      () => Response.json(session),
    )

    const response = await POST(
      post('redeem', { type: 'verification', token }, { [refreshCookie]: 'orv_rt_old.secret' }),
    )

    expect(response.status).toBe(200)
    expect(calls.map((c) => c.path)).toEqual([
      '/v1/account/verification/confirm',
      '/v1/account/sessions/refresh',
    ])
    expect(response.headers.getSetCookie()).toHaveLength(2)
  })

  it('verifies without touching cookies when there is no session', async () => {
    const { POST, calls } = handler(() => Response.json(user))

    const response = await POST(post('redeem', { type: 'email_change', token }))

    expect(response.status).toBe(200)
    expect(calls.map((c) => c.path)).toEqual(['/v1/account/email/confirm'])
    expect(response.headers.getSetCookie()).toEqual([])
  })

  it("passes Orvano's refusal through with its status, code, and Retry-After", async () => {
    const { POST } = handler(
      () =>
        new Response(
          JSON.stringify({
            status: 429,
            code: 'rate_limited',
            detail: 'Slow down.',
            requestId: 'r1',
          }),
          {
            status: 429,
            headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '30' },
          },
        ),
    )

    const response = await POST(post('redeem', { type: 'magic_link', token }))
    const body = (await response.json()) as Record<string, unknown>

    expect(response.status).toBe(429)
    expect(response.headers.get('Retry-After')).toBe('30')
    expect(body).toMatchObject({
      status: 429,
      code: 'rate_limited',
      detail: 'Slow down.',
      requestId: 'r1',
    })
  })

  it('refuses a foreign Origin and a malformed body before calling Orvano', async () => {
    const { POST, calls } = handler(() => Response.json(user))

    const foreign = await POST(
      post('redeem', { type: 'magic_link', token }, {}, 'https://evil.test'),
    )
    const badType = await POST(post('redeem', { type: 'foo', token }))
    const noToken = await POST(post('redeem', { type: 'magic_link' }))

    expect([foreign.status, badType.status, noToken.status]).toEqual([403, 400, 400])
    expect(calls).toHaveLength(0)
  })
})

describe('the email-code action (AC-25)', () => {
  it('signs in with a code and sets both cookies', async () => {
    const { POST, calls } = handler(() =>
      Response.json({ user, session, isNewUser: false }, { status: 201 }),
    )

    const response = await POST(post('email-code', { email: 'ada@example.com', code: '042137' }))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({
      type: 'email_code',
      user,
      isNewUser: false,
      mfaRequired: false,
      factors: [],
    })
    expect(calls[0]?.body).toEqual({ email: 'ada@example.com', code: '042137' })
    expect(response.headers.getSetCookie()).toHaveLength(2)
  })
})

describe('the browser transport (AC-24, AC-25)', () => {
  it('posts links and codes to the route handler and reads the new session from the cookies', async () => {
    const posted: { url: string; body: unknown }[] = []
    vi.stubGlobal('fetch', (url: string, init?: RequestInit) => {
      posted.push({ url, body: JSON.parse(init?.body as string) as unknown })
      return Promise.resolve(Response.json({ type: 'magic_link', user, isNewUser: true }))
    })
    const store = new MemorySessionStore()
    const client = new Client({
      endpoint,
      session: store,
      emailAuth: emailAuthThroughHandler('/auth/orvano'),
      logger: quiet,
    })
    const seen: string[] = []
    client.onAuthStateChange((event) => seen.push(event))

    const result = await client.redeemLink(`${app}/cb?orvano_type=magic_link&orvano_token=${token}`)
    await client.signInWithEmailCode('ada@example.com', '042137')

    expect(result).toEqual({
      type: 'magic_link',
      user,
      isNewUser: true,
      mfaRequired: false,
      factors: [],
    })
    expect(posted).toEqual([
      { url: '/auth/orvano/redeem', body: { type: 'magic_link', token } },
      { url: '/auth/orvano/email-code', body: { email: 'ada@example.com', code: '042137' } },
    ])
    expect(seen).toEqual(['signedIn', 'signedIn'])
  })

  it('throws the handler refusal as an OrvanoError', async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve(Response.json({ code: 'invalid_code', status: 401 }, { status: 401 })),
    )
    const client = new Client({ endpoint, emailAuth: emailAuthThroughHandler(), logger: quiet })

    await expect(client.signInWithEmailCode('ada@example.com', '000000')).rejects.toMatchObject({
      status: 401,
      code: 'invalid_code',
    })
  })
})
