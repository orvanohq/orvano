import { NextRequest } from 'next/server.js'
import { describe, expect, it } from 'vitest'

import { accessCookie, refreshCookie } from '../src/index.js'
import { createOrvanoRouteHandler } from '../src/server.js'

// Spec 0014 AC-36: the route handler's sign-up, anonymous, and anonymous-upgrade actions. A pending
// answer sets no cookie; a permanent upgrade refreshes the cookies so the claim is current.

const endpoint = 'https://orvano.example.com'
const app = 'https://app.example.com'
const quiet = { warn: () => undefined }

function jwt(claims: Record<string, unknown>): string {
  const part = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${part({ alg: 'ES256', kid: 'k' })}.${part(claims)}.c2ln`
}

function session(isAnonymous: boolean, refreshToken = 'orv_rt_new.secret') {
  return {
    accessToken: jwt({
      sub: 'u1',
      sid: 's1',
      exp: Math.floor(Date.now() / 1000) + 900,
      is_anonymous: isAnonymous,
    }),
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshToken,
    refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    sessionId: 's1',
  }
}

const guest = { id: 'u1', email: null, isAnonymous: true }
const permanent = { id: 'u1', email: 'ada@example.com', isAnonymous: false }
const pending = {
  user: null,
  session: null,
  mfa: null,
  isNewUser: false,
  verificationEmail: null,
  verificationRequired: true,
}

interface Call {
  path: string
  headers: Headers
  body: unknown
}

function handler(...answers: (() => Response)[]) {
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
  return {
    calls,
    POST: createOrvanoRouteHandler({ endpoint, project: 'shop', fetch, logger: quiet }).POST,
  }
}

function post(action: string, body: unknown, cookies: Record<string, string> = {}): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(`${app}/api/orvano/${action}`, {
    method: 'POST',
    headers: {
      origin: app,
      'content-type': 'application/json',
      ...(cookie === '' ? {} : { cookie }),
    },
    body: JSON.stringify(body),
  })
}

const sets = (response: Response, name: string): boolean =>
  response.headers.getSetCookie().some((c) => c.startsWith(`${name}=`))

describe('sign up and guests (AC-36)', () => {
  it('sign-up sets both cookies, or none on a pending answer', async () => {
    const signedUp = handler(() =>
      Response.json(
        {
          user: permanent,
          session: session(false),
          mfa: null,
          isNewUser: true,
          verificationEmail: null,
          verificationRequired: false,
        },
        { status: 201 },
      ),
    )
    const created = await signedUp.POST(
      post('sign-up', { email: 'ada@example.com', password: 'correct horse battery', name: 'Ada' }),
    )
    expect(await created.json()).toEqual({
      user: permanent,
      isNewUser: true,
      verificationEmail: null,
    })
    expect(signedUp.calls[0]?.path).toBe('/v1/account')
    expect(signedUp.calls[0]?.body).toEqual({
      email: 'ada@example.com',
      password: 'correct horse battery',
      name: 'Ada',
    })
    expect(sets(created, accessCookie) && sets(created, refreshCookie)).toBe(true)

    const waiting = handler(() => Response.json(pending, { status: 201 }))
    const answer = await waiting.POST(
      post('sign-up', {
        email: 'ada@example.com',
        password: 'correct horse battery',
        verificationRedirectUrl: `${app}/cb`,
      }),
    )
    expect(await answer.json()).toEqual({ verificationRequired: true })
    expect(answer.headers.getSetCookie()).toEqual([])

    const bad = await waiting.POST(post('sign-up', { email: 'ada@example.com' }))
    expect(bad.status).toBe(400)
  })

  it('anonymous signs a guest in and sets both cookies', async () => {
    const { POST, calls } = handler(() =>
      Response.json(
        {
          user: guest,
          session: session(true),
          mfa: null,
          isNewUser: true,
          verificationEmail: null,
          verificationRequired: false,
        },
        { status: 201 },
      ),
    )
    const response = await POST(post('anonymous', {}))
    expect(await response.json()).toEqual({ user: guest, isNewUser: true })
    expect(calls[0]?.path).toBe('/v1/account/sessions/anonymous')
    expect(sets(response, accessCookie) && sets(response, refreshCookie)).toBe(true)
  })

  it('anonymous-upgrade refreshes the cookies once permanent, and changes none while pending', async () => {
    const cookies = {
      [accessCookie]: session(true).accessToken,
      [refreshCookie]: 'orv_rt_old.secret',
    }
    const upgraded = handler(
      () =>
        Response.json({ user: permanent, verificationRequired: false, verificationEmail: null }),
      () => Response.json(session(false)),
    )
    const done = await upgraded.POST(
      post(
        'anonymous-upgrade',
        { email: 'ada@example.com', password: 'correct horse battery' },
        cookies,
      ),
    )
    expect(await done.json()).toEqual({
      user: permanent,
      verificationRequired: false,
      verificationEmail: null,
    })
    expect(upgraded.calls.map((c) => c.path)).toEqual([
      '/v1/account/anonymous/upgrade',
      '/v1/account/sessions/refresh',
    ])
    expect(upgraded.calls[0]?.headers.get('Authorization')).toBe(`Bearer ${cookies[accessCookie]}`)
    expect(sets(done, accessCookie) && sets(done, refreshCookie)).toBe(true)

    const waiting = handler(() =>
      Response.json({ user: null, verificationRequired: true, verificationEmail: null }),
    )
    const answer = await waiting.POST(
      post(
        'anonymous-upgrade',
        {
          email: 'ada@example.com',
          password: 'correct horse battery',
          verificationRedirectUrl: `${app}/cb`,
        },
        cookies,
      ),
    )
    expect(await answer.json()).toEqual({ verificationRequired: true })
    expect(answer.headers.getSetCookie()).toEqual([])
    expect(waiting.calls).toHaveLength(1)

    const signedOut = await waiting.POST(
      post('anonymous-upgrade', { email: 'ada@example.com', password: 'x' }),
    )
    expect(signedOut.status).toBe(401)
  })
})
