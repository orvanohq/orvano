import { NextRequest } from 'next/server.js'
import { describe, expect, it } from 'vitest'

import { accessCookie, oauthCookie, refreshCookie } from '../src/index.js'
import { createOrvanoRouteHandler, safeNext } from '../src/server.js'

// Spec 0012 AC-21: the route handler's provider flow, `POST .../oauth` behind the Origin rule and
// `GET .../oauth-callback` bound by the PKCE cookie, and the `next` rule.

const endpoint = 'https://orvano.example.com'
const app = 'https://app.example.com'
const quiet = { warn: () => undefined }

function jwt(claims: Record<string, unknown>): string {
  const part = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${part({ alg: 'ES256', kid: 'k' })}.${part(claims)}.c2ln`
}

const accessIn = (seconds: number): string =>
  jwt({ sub: 'u', sid: 's1', exp: Math.floor(Date.now() / 1000) + seconds })

const session = {
  accessToken: accessIn(900),
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'orv_rt_new.secret',
  refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  sessionId: 's1',
}

const user = {
  id: 'u',
  email: 'ada@example.com',
  emailVerified: true,
  emailVerifiedAt: null,
  name: null,
  status: 'active',
  metadata: {},
  createdAt: '2026-01-01T00:00:00Z',
  lastSignInAt: null,
  providers: ['google'],
  hasPassword: false,
}

interface Call {
  path: string
  headers: Headers
  body: Record<string, unknown>
}

/** A handler over a fake Orvano that answers by path and records each call. */
function handler(answers: Record<string, () => Response>): {
  GET: (r: NextRequest) => Promise<Response>
  POST: (r: NextRequest) => Promise<Response>
  calls: Call[]
} {
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
  return { ...createOrvanoRouteHandler({ endpoint, project: 'shop', fetch, logger: quiet }), calls }
}

function post(
  path: string,
  body: unknown,
  cookies: Record<string, string> = {},
  origin = app,
): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(`${app}${path}`, {
    method: 'POST',
    headers: { origin, 'content-type': 'application/json', ...(cookie === '' ? {} : { cookie }) },
    body: JSON.stringify(body),
  })
}

function get(pathAndQuery: string, cookies: Record<string, string>): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(`${app}${pathAndQuery}`, { headers: cookie === '' ? {} : { cookie } })
}

/** The `orvano_oauth` value a start set. */
function oauthValue(response: Response): string {
  const cookie = response.headers.getSetCookie().find((c) => c.startsWith(`${oauthCookie}=`))
  return (cookie ?? '').split(';')[0]?.slice(oauthCookie.length + 1) ?? ''
}

describe('createOrvanoRouteHandler oauth (spec 0012, AC-21)', () => {
  const flows = {
    '/v1/account/oauth/flows': () => Response.json({ url: 'https://accounts.example/auth' }),
  }

  it('refuses a foreign Origin before calling Orvano', async () => {
    const { POST, calls } = handler(flows)
    const response = await POST(
      post('/api/orvano/oauth', { provider: 'google' }, {}, 'https://evil.test'),
    )
    expect(response.status).toBe(403)
    expect(calls).toHaveLength(0)
  })

  it('starts a flow with the handler callback and keeps the verifier in an HttpOnly cookie', async () => {
    const { POST, calls } = handler(flows)
    const response = await POST(post('/api/orvano/oauth', { provider: 'github', next: '/account' }))

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ url: 'https://accounts.example/auth' })
    expect(calls[0]?.body.redirectUrl).toBe(`${app}/api/orvano/oauth-callback`)
    expect(calls[0]?.body.codeChallenge).toMatch(/^[A-Za-z0-9_-]{43}$/)
    const cookie =
      response.headers.getSetCookie().find((c) => c.startsWith(`${oauthCookie}=`)) ?? ''
    expect(cookie).toContain('HttpOnly')
    expect(cookie).toContain('Max-Age=720')
    expect(cookie.toLowerCase()).toContain('samesite=lax')
  })

  it('refuses an unknown provider', async () => {
    const { POST } = handler(flows)
    const response = await POST(post('/api/orvano/oauth', { provider: 'yahoo' }))
    expect(response.status).toBe(400)
  })

  it('finishes a sign in: sets both session cookies, clears the flow cookie, and goes to next', async () => {
    const start = handler(flows)
    const started = await start.POST(
      post('/api/orvano/oauth', { provider: 'google', next: '/welcome' }),
    )
    const { GET, calls } = handler({
      '/v1/account/sessions/oauth': () =>
        Response.json({ user, session, isNewUser: true, verificationEmail: null }, { status: 201 }),
    })

    const response = await GET(
      get('/api/orvano/oauth-callback?orvano_type=oauth&orvano_code=orv_oc_x', {
        [oauthCookie]: oauthValue(started),
      }),
    )

    expect(response.status).toBe(303)
    expect(response.headers.get('location')).toBe(`${app}/welcome`)
    expect(calls[0]?.body.code).toBe('orv_oc_x')
    expect(calls[0]?.body.codeVerifier).toMatch(/^[A-Za-z0-9_-]{43}$/)
    const set = response.headers.getSetCookie()
    expect(set.some((c) => c.startsWith(`${accessCookie}=`))).toBe(true)
    expect(set.some((c) => c.startsWith(`${refreshCookie}=orv_rt_new.secret`))).toBe(true)
    expect(set.some((c) => c.startsWith(`${oauthCookie}=;`))).toBe(true)
  })

  it('a link with an expired access cookie refreshes first and links as the user', async () => {
    const start = handler({
      '/v1/account/identities/oauth/flows': () =>
        Response.json({ url: 'https://github.example/auth' }),
      '/v1/account/sessions/refresh': () => Response.json(session),
    })
    const started = await start.POST(
      post(
        '/api/orvano/oauth',
        { provider: 'github', link: true },
        { [refreshCookie]: 'orv_rt_old.secret' },
      ),
    )
    expect(started.status).toBe(200)
    expect(start.calls.map((c) => c.path)).toEqual([
      '/v1/account/sessions/refresh',
      '/v1/account/identities/oauth/flows',
    ])
    expect(start.calls[1]?.headers.get('Authorization')).toBe(`Bearer ${session.accessToken}`)

    const { GET, calls } = handler({
      '/v1/account/sessions/refresh': () => Response.json(session),
      '/v1/account/identities/oauth': () =>
        Response.json({ id: 'i1', provider: 'github' }, { status: 201 }),
    })
    const response = await GET(
      get('/api/orvano/oauth-callback?orvano_type=oauth_link&orvano_code=orv_oc_y', {
        [oauthCookie]: oauthValue(started),
        [accessCookie]: accessIn(10),
        [refreshCookie]: 'orv_rt_old.secret',
      }),
    )
    expect(response.status).toBe(303)
    expect(calls.map((c) => c.path)).toEqual([
      '/v1/account/sessions/refresh',
      '/v1/account/identities/oauth',
    ])
    expect(
      response.headers
        .getSetCookie()
        .some((c) => c.startsWith(`${refreshCookie}=orv_rt_new.secret`)),
    ).toBe(true)
  })

  it('goes to next with orvano_error for a provider error, a missing cookie, or a refusal', async () => {
    const start = handler(flows)
    const started = await start.POST(post('/api/orvano/oauth', { provider: 'google', next: '/in' }))
    const cookie = { [oauthCookie]: oauthValue(started) }
    const { GET } = handler({
      '/v1/account/sessions/oauth': () =>
        Response.json({ code: 'invalid_oauth_code' }, { status: 401 }),
    })

    const denied = await GET(
      get('/api/orvano/oauth-callback?orvano_type=oauth&orvano_error=oauth_access_denied', cookie),
    )
    const missing = await GET(
      get('/api/orvano/oauth-callback?orvano_type=oauth&orvano_code=orv_oc_x', {}),
    )
    const refused = await GET(
      get('/api/orvano/oauth-callback?orvano_type=oauth&orvano_code=orv_oc_x', cookie),
    )

    expect(denied.headers.get('location')).toBe(`${app}/in?orvano_error=oauth_access_denied`)
    expect(missing.headers.get('location')).toBe(`${app}/?orvano_error=invalid_oauth_code`)
    expect(refused.headers.get('location')).toBe(`${app}/in?orvano_error=invalid_oauth_code`)
  })

  it('accepts only a path in the app as next', () => {
    expect(safeNext('/account?tab=1')).toBe('/account?tab=1')
    expect(safeNext('//evil.example')).toBe('/')
    expect(safeNext('/\\evil.example')).toBe('/')
    expect(safeNext('https://evil.example')).toBe('/')
    expect(safeNext(undefined)).toBe('/')
  })
})
