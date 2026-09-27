import { afterEach, describe, expect, it, vi } from 'vitest'

import { Client, LocalStorageSessionStore, MemorySessionStore, Orvano } from '../src/index.js'
import type { AuthEvent, AuthSession, ClientConfig } from '../src/index.js'
import { fakeFetch, health, problem } from './fake-fetch.js'

// Spec 0004 AC-24 and AC-26: the browser store, refresh before a call, one retry after a stale
// token, the failure policy, and the auth state listener.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }

function session(accessToken: string, secondsLeft: number, sessionId = 's1'): AuthSession {
  return {
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + secondsLeft * 1000).toISOString(),
    refreshToken: `orv_rt_${accessToken}`,
    refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    sessionId,
  }
}

function tokens(accessToken: string): () => Response {
  return () => Response.json(session(accessToken, 900))
}

function client(fetch: typeof globalThis.fetch, config: Partial<ClientConfig> = {}): Client {
  return new Client({ endpoint, project: 'shop', fetch, logger: quiet, maxRetries: 0, ...config })
}

function events(c: Client): AuthEvent[] {
  const seen: AuthEvent[] = []
  c.onAuthStateChange((event) => seen.push(event))
  return seen
}

/** A `Storage` in memory, and a way to fire the `storage` event another tab would cause. */
class FakeStorage {
  readonly items = new Map<string, string>()
  getItem(key: string): string | null {
    return this.items.get(key) ?? null
  }
  setItem(key: string, value: string): void {
    this.items.set(key, value)
  }
  removeItem(key: string): void {
    this.items.delete(key)
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('refresh before a call (AC-26)', () => {
  it('refreshes when under 60 seconds of the access token are left, then sends the new token', async () => {
    const { fetch, sent } = fakeFetch(tokens('a2'), health())
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 30)) })
    const seen = events(c)

    await new Orvano(c).health.get()

    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/sessions/refresh')
    expect(sent[0]?.headers.has('Authorization')).toBe(false)
    expect(sent[1]?.headers.get('Authorization')).toBe('Bearer a2')
    expect((await c.session.get())?.accessToken).toBe('a2')
    expect(seen).toEqual(['tokenRefreshed'])
  })

  it('does not refresh while more than 60 seconds are left', async () => {
    const { fetch, sent } = fakeFetch(health())
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 120)) })

    await new Orvano(c).health.get()

    expect(sent).toHaveLength(1)
    expect(sent[0]?.headers.get('Authorization')).toBe('Bearer a1')
  })

  it('refreshes once for calls made at the same time', async () => {
    const { fetch, sent } = fakeFetch(tokens('a2'), health())
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 10)) })

    await Promise.all([new Orvano(c).health.get(), new Orvano(c).health.get()])

    expect(sent.filter((r) => r.url.endsWith('/refresh'))).toHaveLength(1)
  })

  it('keeps the session and goes ahead after a network error', async () => {
    let calls = 0
    const fetch = (input: string | URL | Request): Promise<Response> => {
      calls++
      const url = input instanceof Request ? input.url : input.toString()
      if (url.endsWith('/refresh')) return Promise.reject(new TypeError('offline'))
      return Promise.resolve(health()())
    }
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 10)) })

    await new Orvano(c).health.get()

    expect(calls).toBe(2)
    expect((await c.session.get())?.accessToken).toBe('a1')
  })
})

describe('after a 401 (AC-26)', () => {
  it('refreshes once and repeats the call once on token_expired', async () => {
    const { fetch, sent } = fakeFetch(
      problem(401, { code: 'token_expired' }),
      tokens('a2'),
      health(),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 600)) })

    await new Orvano(c).health.get()

    expect(sent.map((r) => r.headers.get('Authorization'))).toEqual([
      'Bearer a1',
      null,
      'Bearer a2',
    ])
  })

  it('gives up after one repeat', async () => {
    const { fetch, sent } = fakeFetch(
      problem(401, { code: 'invalid_token' }),
      tokens('a2'),
      problem(401, { code: 'invalid_token' }),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 600)) })

    await expect(new Orvano(c).health.get()).rejects.toMatchObject({ code: 'invalid_token' })
    expect(sent).toHaveLength(3)
  })

  it('clears the session and says signedOut when the refresh is refused', async () => {
    const { fetch } = fakeFetch(
      problem(401, { code: 'token_expired' }),
      problem(401, { code: 'invalid_refresh_token' }),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 600)) })
    const seen = events(c)

    await expect(new Orvano(c).health.get()).rejects.toMatchObject({ code: 'token_expired' })
    expect(await c.session.get()).toBeNull()
    expect(seen).toEqual(['signedOut'])
  })

  it('never refreshes after a 401 for anything else', async () => {
    const { fetch, sent } = fakeFetch(problem(401, { code: 'session_required' }))
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 600)) })

    await expect(new Orvano(c).health.get()).rejects.toMatchObject({ code: 'session_required' })
    expect(sent).toHaveLength(1)
  })
})

describe('auth state listener (AC-26)', () => {
  it('says signedIn, userUpdated, and signedOut as the contract marks the calls', async () => {
    const signedIn = session('a1', 900)
    const { fetch } = fakeFetch(
      () => Response.json({ user: { id: 'u' }, session: signedIn }),
      () => Response.json({ id: 'u' }),
      () => new Response(null, { status: 204 }),
      () => Response.json({ user: { id: 'u' }, session: signedIn }),
    )
    const c = client(fetch, { session: new MemorySessionStore() })
    const seen: [AuthEvent, string | undefined][] = []
    const stop = c.onAuthStateChange((event, s) => seen.push([event, s?.accessToken]))

    await c.request({ method: 'POST', path: '/v1/account/sessions/password', session: 'start' })
    await c.request({ method: 'PATCH', path: '/v1/account', session: 'user' })
    await c.request({ method: 'DELETE', path: '/v1/account/sessions/current', session: 'end' })
    stop()
    await c.request({ method: 'POST', path: '/v1/account/sessions/password', session: 'start' })

    expect(seen).toEqual([
      ['signedIn', 'a1'],
      ['userUpdated', 'a1'],
      ['signedOut', undefined],
    ])
  })

  it('getSession refreshes a session about to expire', async () => {
    const { fetch } = fakeFetch(tokens('a2'))
    const c = client(fetch, { session: new MemorySessionStore(session('a1', 5)) })

    expect((await c.getSession())?.accessToken).toBe('a2')
  })
})

describe('in a browser (AC-24)', () => {
  it('keeps the session in localStorage under orvano.session.<project>', async () => {
    const storage = new FakeStorage()
    vi.stubGlobal('document', {})
    vi.stubGlobal('localStorage', storage)
    const signedIn = session('a1', 900)
    const { fetch } = fakeFetch(() => Response.json({ user: { id: 'u' }, session: signedIn }))
    const c = client(fetch)

    await c.request({ method: 'POST', path: '/v1/account/sessions/password', session: 'start' })

    expect(c.session).toBeInstanceOf(LocalStorageSessionStore)
    expect(JSON.parse(storage.getItem('orvano.session.shop') ?? 'null')).toEqual(signedIn)
  })

  it('refreshes under the Web Lock orvano.refresh.<project>, and skips it when another tab already did', async () => {
    const storage = new FakeStorage()
    const store = new LocalStorageSessionStore('shop', storage)
    store.set(session('a1', 10))
    const locks: string[] = []
    vi.stubGlobal('navigator', {
      locks: {
        request: async <T>(name: string, fn: () => Promise<T>): Promise<T> => {
          locks.push(name)
          // Another tab refreshed while this one waited for the lock.
          store.set(session('a9', 900))
          return fn()
        },
      },
    })
    const { fetch, sent } = fakeFetch(health())
    const c = client(fetch, { session: store })

    await new Orvano(c).health.get()

    expect(locks).toEqual(['orvano.refresh.shop'])
    expect(sent).toHaveLength(1)
    expect(sent[0]?.headers.get('Authorization')).toBe('Bearer a9')
  })

  it('tells listeners about changes another tab made', () => {
    const storage = new FakeStorage()
    const handlers: ((e: { key: string | null; newValue: string | null }) => void)[] = []
    vi.stubGlobal('addEventListener', (type: string, fn: (typeof handlers)[number]) => {
      if (type === 'storage') handlers.push(fn)
    })
    vi.stubGlobal('removeEventListener', () => undefined)
    const store = new LocalStorageSessionStore('shop', storage)
    store.set(session('a1', 900))
    const c = client(fakeFetch(health()).fetch, { session: store })
    const seen = events(c)
    const fire = (value: AuthSession | null): void => {
      for (const handler of handlers)
        handler({
          key: 'orvano.session.shop',
          newValue: value === null ? null : JSON.stringify(value),
        })
    }

    fire(session('a2', 900))
    fire(session('b1', 900, 's2'))
    fire(null)
    handlers.forEach((h) => {
      h({ key: 'something.else', newValue: null })
    })

    expect(seen).toEqual(['tokenRefreshed', 'signedIn', 'signedOut'])
  })
})
