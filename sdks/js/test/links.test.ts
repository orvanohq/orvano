import { afterEach, describe, expect, it, vi } from 'vitest'

import { Client, MemorySessionStore, OrvanoError, readEmailLink } from '../src/index.js'
import type { AuthEvent, AuthSession, ClientConfig } from '../src/index.js'
import { fakeFetch, problem } from './fake-fetch.js'

// Spec 0010 AC-14, AC-24, AC-26: the link helper, email code sign in, the claim refresh, and
// retryAfter on the error.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }
const token = `orv_el_${'A'.repeat(43)}`

/** An unsigned JWT with the claims the client reads; the client never checks signatures. */
function jwt(claims: Record<string, unknown>): string {
  const encode = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${encode({ alg: 'ES256' })}.${encode(claims)}.sig`
}

function session(sub: string, emailVerified: boolean, sessionId = 's1'): AuthSession {
  return {
    accessToken: jwt({ sub, sid: sessionId, email_verified: emailVerified }),
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshToken: 'orv_rt_x',
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    sessionId,
  }
}

const user = (id: string, emailVerified: boolean): Record<string, unknown> => ({
  id,
  email: 'ada@example.com',
  emailVerified,
})

function client(fetch: typeof globalThis.fetch, config: Partial<ClientConfig> = {}): Client {
  return new Client({ endpoint, project: 'shop', fetch, logger: quiet, maxRetries: 0, ...config })
}

function events(c: Client): AuthEvent[] {
  const seen: AuthEvent[] = []
  c.onAuthStateChange((event) => seen.push(event))
  return seen
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('reading a link (AC-24)', () => {
  it('returns null for a URL with neither parameter', () => {
    expect(readEmailLink('https://app.example.com/cb?next=1')).toBeNull()
    expect(readEmailLink(new URLSearchParams())).toBeNull()
  })

  it('throws before any call for an unknown type, a missing token, or a reset without a password', () => {
    expect(() => readEmailLink(`https://a.example/?orvano_type=foo&orvano_token=${token}`)).toThrow(
      TypeError,
    )
    expect(() => readEmailLink('https://a.example/?orvano_type=magic_link')).toThrow(TypeError)
    expect(() => readEmailLink(`https://a.example/?orvano_token=${token}`)).toThrow(TypeError)
    expect(() =>
      readEmailLink(`https://a.example/?orvano_type=recovery&orvano_token=${token}`),
    ).toThrow(TypeError)
    expect(
      readEmailLink(new URL(`https://a.example/?orvano_type=recovery&orvano_token=${token}`), 'pw'),
    ).toEqual({ type: 'recovery', token, password: 'pw' })
  })

  it('redeemLink returns null, and throws without calling, as readEmailLink does', async () => {
    const { fetch, sent } = fakeFetch(() => Response.json({}))
    const c = client(fetch)

    expect(await c.redeemLink('https://app.example.com/cb')).toBeNull()
    await expect(
      c.redeemLink(`https://app.example.com/cb?orvano_type=foo&orvano_token=${token}`),
    ).rejects.toThrow(TypeError)
    expect(sent).toHaveLength(0)
  })
})

describe('redeeming a link (AC-24)', () => {
  it('a magic link stores the new session, replacing any, and says signedIn', async () => {
    const fresh = session('u2', true, 's2')
    const { fetch, sent } = fakeFetch(() =>
      Response.json({ user: user('u2', true), session: fresh, isNewUser: true }, { status: 201 }),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('u1', false)) })
    const seen = events(c)

    const result = await c.redeemLink(
      `https://app.example.com/cb?orvano_type=magic_link&orvano_token=${token}`,
    )

    expect(result).toEqual({
      type: 'magic_link',
      user: user('u2', true),
      isNewUser: true,
      mfaRequired: false,
      factors: [],
    })
    expect((await c.session.get())?.sessionId).toBe('s2')
    expect(seen).toEqual(['signedIn'])
    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/sessions/magic-link')
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ token })
  })

  it('a reset sends the new password and signs in', async () => {
    const { fetch, sent } = fakeFetch(() =>
      Response.json({
        user: user('u1', true),
        session: session('u1', true, 's9'),
        isNewUser: false,
      }),
    )
    const c = client(fetch, { session: new MemorySessionStore() })

    const result = await c.redeemLink(
      new URLSearchParams({ orvano_type: 'recovery', orvano_token: token }),
      { password: 'another horse battery' },
    )

    expect(result?.type).toBe('recovery')
    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/recovery/confirm')
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ token, password: 'another horse battery' })
    expect((await c.session.get())?.sessionId).toBe('s9')
  })

  it('a verification for the signed in user refreshes, so the claim is current, and says userUpdated', async () => {
    const { fetch, sent } = fakeFetch(
      () => Response.json(user('u1', true)),
      () => Response.json(session('u1', true, 's1')),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('u1', false)) })
    const seen = events(c)

    const result = await c.redeemLink(
      `https://app.example.com/cb?orvano_type=verification&orvano_token=${token}`,
    )

    expect(result).toEqual({
      type: 'verification',
      user: user('u1', true),
      isNewUser: false,
      mfaRequired: false,
      factors: [],
    })
    expect(sent.map((s) => new URL(s.url).pathname)).toEqual([
      '/v1/account/verification/confirm',
      '/v1/account/sessions/refresh',
    ])
    expect(seen).toEqual(['tokenRefreshed', 'userUpdated'])
  })

  it('an email change whose claim is already current says userUpdated without a refresh', async () => {
    const { fetch, sent } = fakeFetch(() => Response.json(user('u1', true)))
    const c = client(fetch, { session: new MemorySessionStore(session('u1', true)) })
    const seen = events(c)

    await c.redeemLink(`https://a.example/?orvano_type=email_change&orvano_token=${token}`)

    expect(sent.map((s) => new URL(s.url).pathname)).toEqual(['/v1/account/email/confirm'])
    expect(seen).toEqual(['userUpdated'])
  })

  it('an email change of another user, or with no session, changes nothing here', async () => {
    const { fetch, sent } = fakeFetch(() => Response.json(user('someone-else', true)))
    const signedIn = client(fetch, { session: new MemorySessionStore(session('u1', false)) })
    const nobody = client(fetch, { session: new MemorySessionStore() })
    const seen = [...events(signedIn), ...events(nobody)]

    await signedIn.redeemLink(`https://a.example/?orvano_type=email_change&orvano_token=${token}`)
    await nobody.redeemLink(`https://a.example/?orvano_type=email_change&orvano_token=${token}`)

    expect(sent.map((s) => new URL(s.url).pathname)).toEqual([
      '/v1/account/email/confirm',
      '/v1/account/email/confirm',
    ])
    expect(seen).toEqual([])
  })

  it('a used link throws the server refusal and keeps the session', async () => {
    const { fetch } = fakeFetch(problem(401, { code: 'invalid_email_token' }))
    const before = session('u1', false)
    const c = client(fetch, { session: new MemorySessionStore(before) })

    await expect(
      c.redeemLink(`https://a.example/?orvano_type=magic_link&orvano_token=${token}`),
    ).rejects.toMatchObject({ status: 401, code: 'invalid_email_token' })
    expect(await c.session.get()).toEqual(before)
  })

  it('in a browser it reads location.href and removes the two parameters after a success', async () => {
    const replaced: string[] = []
    vi.stubGlobal('location', {
      href: `https://app.example.com/cb?next=%2Fhome&orvano_type=magic_link&orvano_token=${token}#top`,
    })
    vi.stubGlobal('history', {
      state: null,
      replaceState: (_data: unknown, _unused: string, url?: string) => replaced.push(url ?? ''),
    })
    const { fetch } = fakeFetch(() =>
      Response.json({ user: user('u1', true), session: session('u1', true), isNewUser: false }),
    )

    await client(fetch, { session: new MemorySessionStore() }).redeemLink()

    expect(replaced).toEqual(['https://app.example.com/cb?next=%2Fhome#top'])
  })

  it('leaves the address bar alone when it fails or the URL was passed in', async () => {
    const replaced: string[] = []
    vi.stubGlobal('location', {
      href: `https://app.example.com/cb?orvano_type=magic_link&orvano_token=${token}`,
    })
    vi.stubGlobal('history', { state: null, replaceState: () => replaced.push('x') })
    const failing = fakeFetch(problem(401, { code: 'invalid_email_token' }))
    const working = fakeFetch(() =>
      Response.json({ user: user('u1', true), session: session('u1', true), isNewUser: false }),
    )

    await expect(client(failing.fetch).redeemLink()).rejects.toBeInstanceOf(OrvanoError)
    await client(working.fetch, { session: new MemorySessionStore() }).redeemLink(
      `https://elsewhere.example/?orvano_type=magic_link&orvano_token=${token}`,
    )

    expect(replaced).toEqual([])
  })
})

describe('email code sign in', () => {
  it('stores the session and says signedIn', async () => {
    const { fetch, sent } = fakeFetch(() =>
      Response.json({
        user: user('u1', true),
        session: session('u1', true, 's3'),
        isNewUser: true,
      }),
    )
    const c = client(fetch, { session: new MemorySessionStore() })
    const seen = events(c)

    const result = await c.signInWithEmailCode('ada@example.com', '042137')

    expect(result).toEqual({
      user: user('u1', true),
      isNewUser: true,
      mfaRequired: false,
      factors: [],
    })
    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/sessions/email-code')
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ email: 'ada@example.com', code: '042137' })
    expect(seen).toEqual(['signedIn'])
  })

  it('goes through a custom transport when one is set', async () => {
    const transport = {
      redeemLink: vi.fn(),
      signInWithEmailCode: vi.fn(() =>
        Promise.resolve({ user: user('u1', true), isNewUser: false }),
      ),
    }
    const { fetch, sent } = fakeFetch(() => Response.json({}))
    const c = client(fetch, { emailAuth: transport })

    await c.signInWithEmailCode('ada@example.com', '042137')

    expect(transport.signInWithEmailCode).toHaveBeenCalledOnce()
    expect(sent).toHaveLength(0)
  })
})

describe('retryAfter (AC-26)', () => {
  it('reads Retry-After seconds or a date, and null without one', async () => {
    const seconds = fakeFetch(problem(429, { code: 'rate_limited' }, undefined, '42'))
    const date = fakeFetch(
      problem(
        429,
        { code: 'email_rate_limited' },
        undefined,
        new Date(Date.now() + 90_000).toUTCString(),
      ),
    )
    const none = fakeFetch(problem(401, { code: 'invalid_code' }))

    await expect(
      client(seconds.fetch).request({ method: 'POST', path: '/v1/x' }),
    ).rejects.toMatchObject({ retryAfter: 42 })
    const fromDate = await client(date.fetch)
      .request({ method: 'POST', path: '/v1/x' })
      .catch((e: unknown) => e)
    expect((fromDate as OrvanoError).retryAfter).toBeGreaterThanOrEqual(88)
    await expect(
      client(none.fetch).request({ method: 'POST', path: '/v1/x' }),
    ).rejects.toMatchObject({ retryAfter: null })
  })
})
