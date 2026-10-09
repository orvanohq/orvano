import { NextRequest } from 'next/server.js'
import { describe, expect, it } from 'vitest'

import { accessCookie, forwardedClientHeaders, refreshCookie, secureCookies } from '../src/index.js'
import { createOrvanoRouteHandler, updateSession } from '../src/server.js'

// Spec 0004 AC-23 and AC-31: the middleware helper, the route handler the browser client uses,
// the Secure rule, and forwarding the browser's IP and user agent.

const endpoint = 'https://orvano.example.com'
const app = 'https://app.example.com'
const quiet = { warn: () => undefined }

function jwt(claims: Record<string, unknown>): string {
  const part = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${part({ alg: 'ES256', kid: 'k' })}.${part(claims)}.c2ln`
}

function accessExpiringIn(seconds: number): string {
  return jwt({ sub: 'u', sid: 's1', exp: Math.floor(Date.now() / 1000) + seconds })
}

const fresh = {
  accessToken: accessExpiringIn(900),
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'orv_rt_new.secret',
  refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
  sessionId: 's1',
}

interface Call {
  url: string
  method: string
  headers: Headers
  body: unknown
}

/** A fake Orvano: answers each call with `answer` and records it. */
function orvano(answer: () => Response): { fetch: typeof fetch; calls: Call[] } {
  const calls: Call[] = []
  const fetch = (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    calls.push({
      url: input instanceof Request ? input.url : input.toString(),
      method: init?.method ?? 'GET',
      headers: new Headers(init?.headers),
      body: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined,
    })
    return Promise.resolve(answer())
  }
  return { fetch, calls }
}

function request(
  url: string,
  cookies: Record<string, string>,
  headers: Record<string, string> = {},
): NextRequest {
  const cookie = Object.entries(cookies)
    .map(([k, v]) => `${k}=${v}`)
    .join('; ')
  return new NextRequest(url, {
    method: 'POST',
    headers: { ...(cookie === '' ? {} : { cookie }), ...headers },
  })
}

function setCookies(response: Response): string[] {
  return response.headers.getSetCookie()
}

describe('updateSession (AC-23)', () => {
  it('refreshes when under 60 seconds are left and sets both cookies on the request and response', async () => {
    const { fetch, calls } = orvano(() => Response.json(fresh))
    const incoming = request(
      `${app}/dashboard`,
      { [accessCookie]: accessExpiringIn(30), [refreshCookie]: 'orv_rt_old.secret' },
      { 'x-forwarded-for': '6.6.6.6, 203.0.113.7', 'user-agent': 'Browser/1' },
    )

    const response = await updateSession(incoming, {
      endpoint,
      project: 'shop',
      fetch,
      logger: quiet,
    })

    expect(calls).toHaveLength(1)
    expect(calls[0]?.body).toEqual({ refreshToken: 'orv_rt_old.secret' })
    expect(calls[0]?.headers.get('X-Orvano-Client-IP')).toBe('203.0.113.7')
    expect(calls[0]?.headers.get('X-Orvano-Client-UA')).toBe('Browser/1')
    const cookies = setCookies(response)
    const accessHeader = cookies.find((c) => c.startsWith(`${accessCookie}=`)) ?? ''
    const refreshHeader = cookies.find((c) => c.startsWith(`${refreshCookie}=`)) ?? ''
    expect(accessHeader).toContain(fresh.accessToken)
    expect(accessHeader).toMatch(/Secure/i)
    expect(accessHeader).toMatch(/SameSite=lax/i)
    expect(accessHeader).not.toMatch(/HttpOnly/i)
    expect(refreshHeader).toContain('orv_rt_new.secret')
    expect(refreshHeader).toMatch(/HttpOnly/i)
    expect([accessHeader, refreshHeader].some((c) => /Domain=/i.test(c))).toBe(false)
    expect(incoming.cookies.get(accessCookie)?.value).toBe(fresh.accessToken)
  })

  it('leaves a fresh session alone', async () => {
    const { fetch, calls } = orvano(() => Response.json(fresh))

    await updateSession(
      request(`${app}/`, {
        [accessCookie]: accessExpiringIn(600),
        [refreshCookie]: 'orv_rt_old.secret',
      }),
      { endpoint, fetch, logger: quiet },
    )

    expect(calls).toHaveLength(0)
  })

  it('clears both cookies when Orvano refuses the refresh', async () => {
    const { fetch } = orvano(() =>
      Response.json({ code: 'invalid_refresh_token' }, { status: 401 }),
    )

    const response = await updateSession(
      request(`${app}/`, { [refreshCookie]: 'orv_rt_old.secret' }),
      {
        endpoint,
        fetch,
        logger: quiet,
      },
    )

    const cookies = setCookies(response)
    expect(
      cookies.some(
        (c) =>
          c.startsWith(`${accessCookie}=;`) ||
          (c.startsWith(`${accessCookie}=`) && /Max-Age=0|Expires=Thu, 01 Jan 1970/i.test(c)),
      ),
    ).toBe(true)
    expect(
      cookies.some(
        (c) => c.startsWith(`${refreshCookie}=`) && /Max-Age=0|Expires=Thu, 01 Jan 1970/i.test(c),
      ),
    ).toBe(true)
  })

  it('turns Secure off only for http://localhost', async () => {
    const { fetch } = orvano(() => Response.json(fresh))

    const response = await updateSession(
      request('http://localhost:3000/', { [refreshCookie]: 'orv_rt_old.secret' }),
      {
        endpoint,
        fetch,
        logger: quiet,
      },
    )

    expect(setCookies(response).every((c) => !/Secure/i.test(c))).toBe(true)
    expect(secureCookies('http://localhost:3000')).toBe(false)
    expect(secureCookies('http://127.0.0.1:3000')).toBe(true)
    expect(secureCookies('https://localhost')).toBe(true)
  })
})

describe('createOrvanoRouteHandler (AC-23)', () => {
  const handler = (answer: () => Response) => {
    const fake = orvano(answer)
    return {
      ...fake,
      POST: createOrvanoRouteHandler({
        endpoint,
        project: 'shop',
        fetch: fake.fetch,
        logger: quiet,
      }).POST,
    }
  }

  it('refuses a request with a foreign or no Origin before reading a cookie', async () => {
    const { POST, calls } = handler(() => Response.json(fresh))
    const cookies = { [refreshCookie]: 'orv_rt_old.secret' }

    const foreign = await POST(
      request(`${app}/api/orvano/refresh`, cookies, { origin: 'https://evil.test' }),
    )
    const none = await POST(request(`${app}/api/orvano/refresh`, cookies))

    expect([foreign.status, none.status]).toEqual([403, 403])
    expect(calls).toHaveLength(0)
    expect(setCookies(foreign)).toEqual([])
  })

  it('refreshes: sets both cookies and answers only the access token', async () => {
    const { POST } = handler(() => Response.json(fresh))

    const response = await POST(
      request(
        `${app}/api/orvano/refresh`,
        { [refreshCookie]: 'orv_rt_old.secret' },
        { origin: app },
      ),
    )
    const body = (await response.json()) as Record<string, unknown>

    expect(response.status).toBe(200)
    expect(body).toEqual({
      accessToken: fresh.accessToken,
      accessTokenExpiresAt: fresh.accessTokenExpiresAt,
      sessionId: 's1',
    })
    expect(
      setCookies(response).some((c) => c.startsWith(`${refreshCookie}=orv_rt_new.secret`)),
    ).toBe(true)
  })

  it('answers 401 and clears the cookies when the refresh is refused or there is none', async () => {
    const { POST } = handler(() =>
      Response.json({ code: 'invalid_refresh_token' }, { status: 401 }),
    )

    const refused = await POST(
      request(`${app}/api/orvano/refresh`, { [refreshCookie]: 'x' }, { origin: app }),
    )
    const missing = await POST(request(`${app}/api/orvano/refresh`, {}, { origin: app }))

    expect([refused.status, missing.status]).toEqual([401, 401])
    expect(((await refused.json()) as { code: string }).code).toBe('invalid_refresh_token')
  })

  it('signs out: ends the session as the user and clears both cookies', async () => {
    const { POST, calls } = handler(() => new Response(null, { status: 204 }))
    const access = accessExpiringIn(600)

    const response = await POST(
      request(
        `${app}/api/orvano/signout`,
        { [accessCookie]: access, [refreshCookie]: 'x' },
        { origin: app },
      ),
    )

    expect(response.status).toBe(204)
    expect(calls[0]?.method).toBe('DELETE')
    expect(new URL(calls[0]?.url ?? '').pathname).toBe('/v1/account/sessions/current')
    expect(calls[0]?.headers.get('Authorization')).toBe(`Bearer ${access}`)
    expect(setCookies(response)).toHaveLength(2)
  })
})

describe('forwardedClientHeaders (spec 0004 AC-31, spec 0014 AC-36)', () => {
  const headers = (values: Record<string, string>) => new Headers(values)

  it('takes x-real-ip, else the rightmost x-forwarded-for value, and the user agent', () => {
    // The first value is whatever the visitor sent; the last is what the nearest proxy appended.
    expect(
      forwardedClientHeaders(
        headers({ 'x-forwarded-for': ' 6.6.6.6 , 198.51.100.1 ', 'user-agent': 'UA' }),
      ),
    ).toEqual({
      'X-Orvano-Client-IP': '198.51.100.1',
      'X-Orvano-Client-UA': 'UA',
    })
    expect(
      forwardedClientHeaders(
        headers({ 'x-real-ip': '198.51.100.2', 'x-forwarded-for': '6.6.6.6, 10.0.0.9' }),
      ),
    ).toEqual({ 'X-Orvano-Client-IP': '198.51.100.2' })
    expect(forwardedClientHeaders(headers({}))).toEqual({})
  })

  it('uses a clientIp option in place of the default', () => {
    const fromCdn = (request: { headers: { get(name: string): string | null } }) =>
      request.headers.get('cf-connecting-ip')
    expect(
      forwardedClientHeaders(
        headers({ 'cf-connecting-ip': '203.0.113.9', 'x-real-ip': '10.0.0.1' }),
        fromCdn,
      ),
    ).toEqual({ 'X-Orvano-Client-IP': '203.0.113.9' })
    expect(forwardedClientHeaders(headers({ 'x-real-ip': '10.0.0.1' }), () => null)).toEqual({})
  })
})
