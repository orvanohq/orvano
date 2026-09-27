import { describe, expect, it } from 'vitest'

import {
  CookieSessionStore,
  accessCookie,
  createBrowserClient,
  createMiddlewareClient,
  createServerClient,
  refreshCookie,
} from '../src/index.js'
import type { AuthSession, CookieOptions, CookieStore } from '../src/index.js'

const endpoint = 'https://orvano.example.com'
const quiet = { warn: () => undefined }

/** An unsigned JWT with the claims the cookie store reads; the store never checks signatures. */
function jwt(claims: Record<string, unknown>): string {
  const part = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${part({ alg: 'ES256', kid: 'k' })}.${part(claims)}.c2ln`
}

const access = jwt({ sub: 'u', sid: 's1', exp: 1893456000 })
const session: AuthSession = {
  accessToken: access,
  accessTokenExpiresAt: '2030-01-01T00:00:00.000Z',
  refreshToken: 'orv_rt_x.y',
  refreshTokenExpiresAt: '2030-01-31T00:00:00.000Z',
  sessionId: 's1',
}

/** A Next.js style cookie jar that records writes. */
function jar(
  initial: Record<string, string> = {},
): CookieStore & { values: Map<string, string>; options: Map<string, CookieOptions> } {
  const values = new Map(Object.entries(initial))
  const options = new Map<string, CookieOptions>()
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

/** A fake fetch answering health and recording the Authorization header each call carried. */
function recordingFetch(): { fetch: typeof fetch; bearers: (string | null)[] } {
  const bearers: (string | null)[] = []
  const fetch = (_input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    bearers.push(new Headers(init?.headers).get('Authorization'))
    return Promise.resolve(Response.json({ status: 'ok', version: '0.0.0' }))
  }
  return { fetch, bearers }
}

describe('createServerClient (spec 0004 AC-23)', () => {
  it('sends the access token from the orvano_access cookie as a bearer', async () => {
    const { fetch, bearers } = recordingFetch()

    await createServerClient({
      endpoint,
      fetch,
      logger: quiet,
      cookies: jar({ [accessCookie]: access }),
    }).health.get()

    expect(bearers).toEqual([`Bearer ${access}`])
  })

  it('sends no session when the cookie is missing, empty, or not a token', async () => {
    const { fetch, bearers } = recordingFetch()

    for (const cookies of [jar(), jar({ [accessCookie]: '' }), jar({ [accessCookie]: 'x' })])
      await createServerClient({ endpoint, fetch, logger: quiet, cookies }).health.get()

    expect(bearers).toEqual([null, null, null])
  })
})

describe('CookieSessionStore (spec 0004 AC-23)', () => {
  it('writes the access token to a readable cookie and the refresh token to an HttpOnly one', () => {
    const cookies = jar()

    new CookieSessionStore(cookies).set(session)

    expect(cookies.values.get(accessCookie)).toBe(access)
    expect(cookies.values.get(refreshCookie)).toBe('orv_rt_x.y')
    expect(cookies.options.get(accessCookie)).toEqual({
      httpOnly: false,
      secure: true,
      sameSite: 'lax',
      path: '/',
      expires: new Date('2030-01-01T00:00:00Z'),
    })
    expect(cookies.options.get(refreshCookie)).toEqual({
      httpOnly: true,
      secure: true,
      sameSite: 'lax',
      path: '/',
      expires: new Date('2030-01-31T00:00:00Z'),
    })
    expect([...cookies.options.values()].every((o) => !('domain' in o))).toBe(true)
  })

  it('reads the session back from both cookies', () => {
    const store = new CookieSessionStore(
      jar({ [accessCookie]: access, [refreshCookie]: 'orv_rt_x.y' }),
    )

    expect(store.get()).toEqual({ ...session, refreshTokenExpiresAt: null })
  })

  it('deletes both cookies when the session is cleared', () => {
    const cookies = jar({ [accessCookie]: access, [refreshCookie]: 'orv_rt_x.y' })

    new CookieSessionStore(cookies).set(null)

    expect(cookies.values.size).toBe(0)
  })

  it('reads only where it cannot write, as in a server component', () => {
    const readOnly: CookieStore = {
      get: (name) => (name === accessCookie ? { value: access } : undefined),
      set: () => {
        throw new Error('Cookies can only be modified in a Server Action or Route Handler')
      },
    }
    const store = new CookieSessionStore(readOnly)

    expect(() => {
      store.set(session)
    }).not.toThrow()
    expect(store.get()?.accessToken).toBe(access)
  })
})

describe('createMiddlewareClient (spec 0004 AC-23)', () => {
  it('reads the request cookie and writes a new session to the request and the response', async () => {
    const request = jar({ [accessCookie]: access })
    const response = jar()
    const { fetch, bearers } = recordingFetch()
    const orvano = createMiddlewareClient({
      endpoint,
      fetch,
      logger: quiet,
      request: { cookies: request },
      response: { cookies: response },
    })

    await orvano.health.get()
    const next = jwt({ sub: 'u', sid: 's1', exp: 1893457000 })
    await orvano.client.session.set({ ...session, accessToken: next })

    expect(bearers).toEqual([`Bearer ${access}`])
    expect([request.values.get(accessCookie), response.values.get(accessCookie)]).toEqual([
      next,
      next,
    ])
  })
})

describe('createBrowserClient', () => {
  it('shares one client per endpoint and project', () => {
    const a = createBrowserClient({ endpoint, project: 'p1' })

    expect(createBrowserClient({ endpoint, project: 'p1' })).toBe(a)
    expect(createBrowserClient({ endpoint, project: 'p2' })).not.toBe(a)
  })
})
