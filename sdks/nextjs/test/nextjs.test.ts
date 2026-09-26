import { describe, expect, it } from 'vitest'

import {
  CookieSessionStore,
  createBrowserClient,
  createMiddlewareClient,
  createServerClient,
  sessionCookie,
} from '../src/index.js'
import type { CookieOptions, CookieStore } from '../src/index.js'

const endpoint = 'https://orvano.example.com'
const quiet = { warn: () => undefined }

/** A Next.js style cookie jar that records writes. */
function jar(
  initial: Record<string, string> = {},
): CookieStore & { values: Map<string, string>; options: CookieOptions[] } {
  const values = new Map(Object.entries(initial))
  const options: CookieOptions[] = []
  return {
    values,
    options,
    get: (name) => {
      const value = values.get(name)
      return value === undefined ? undefined : { value }
    },
    set: (name, value, opts) => {
      values.set(name, value)
      options.push(opts)
    },
    delete: (name) => values.delete(name),
  }
}

/** A fake fetch answering health and recording the session header each call carried. */
function recordingFetch(): { fetch: typeof fetch; sessions: (string | null)[] } {
  const sessions: (string | null)[] = []
  const fetch = (_input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    sessions.push(new Headers(init?.headers).get('X-Orvano-Session'))
    return Promise.resolve(Response.json({ status: 'ok', version: '0.0.0' }))
  }
  return { fetch, sessions }
}

describe('createServerClient (AC-5)', () => {
  it('forwards the orvano_session cookie as the session header', async () => {
    const { fetch, sessions } = recordingFetch()

    await createServerClient({
      endpoint,
      fetch,
      logger: quiet,
      cookies: jar({ [sessionCookie]: 't' }),
    }).health.get()

    expect(sessions).toEqual(['t'])
  })

  it('sends no session when the cookie is missing or empty', async () => {
    const { fetch, sessions } = recordingFetch()

    await createServerClient({ endpoint, fetch, logger: quiet, cookies: jar() }).health.get()
    await createServerClient({
      endpoint,
      fetch,
      logger: quiet,
      cookies: jar({ [sessionCookie]: '' }),
    }).health.get()

    expect(sessions).toEqual([null, null])
  })
})

describe('CookieSessionStore (AC-5)', () => {
  it('writes the session as an HttpOnly, Secure, SameSite=Lax cookie on the whole site', () => {
    const cookies = jar()

    new CookieSessionStore(cookies).set('t')

    expect(cookies.values.get('orvano_session')).toBe('t')
    expect(cookies.options).toEqual([{ httpOnly: true, secure: true, sameSite: 'lax', path: '/' }])
  })

  it('deletes the cookie when the session is cleared', () => {
    const cookies = jar({ [sessionCookie]: 't' })

    new CookieSessionStore(cookies).set(null)

    expect(cookies.values.has(sessionCookie)).toBe(false)
  })

  it('reads only where it cannot write, as in a server component', () => {
    const readOnly: CookieStore = {
      get: () => ({ value: 't' }),
      set: () => {
        throw new Error('Cookies can only be modified in a Server Action or Route Handler')
      },
    }
    const store = new CookieSessionStore(readOnly)

    expect(() => {
      store.set('new')
    }).not.toThrow()
    expect(store.get()).toBe('t')
  })
})

describe('createMiddlewareClient (AC-5)', () => {
  it('reads the request cookie and writes a new session to the request and the response', async () => {
    const request = jar({ [sessionCookie]: 't' })
    const response = jar()
    const { fetch, sessions } = recordingFetch()
    const orvano = createMiddlewareClient({
      endpoint,
      fetch,
      logger: quiet,
      request: { cookies: request },
      response: { cookies: response },
    })

    await orvano.health.get()
    await orvano.client.session.set('t2')

    expect(sessions).toEqual(['t'])
    expect([request.values.get(sessionCookie), response.values.get(sessionCookie)]).toEqual([
      't2',
      't2',
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
