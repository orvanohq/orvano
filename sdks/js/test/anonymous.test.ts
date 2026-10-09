import { describe, expect, it, vi } from 'vitest'

import { AccountService, Client, MemorySessionStore } from '../src/index.js'
import type { AuthEvent, AuthSession, ClientConfig } from '../src/index.js'
import { fakeFetch, problem } from './fake-fetch.js'

// Spec 0014 AC-36: guest sign in stores its session through the generated call, and an upgrade
// refreshes the session so the next token says the user is permanent, unless it waits for its link.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }

/** An unsigned JWT with the claims the client reads; the client never checks signatures. */
function jwt(claims: Record<string, unknown>): string {
  const encode = (value: unknown): string =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${encode({ alg: 'ES256' })}.${encode(claims)}.sig`
}

function session(sub: string, isAnonymous: boolean, sessionId = 's1'): AuthSession {
  return {
    accessToken: jwt({ sub, sid: sessionId, email_verified: false, is_anonymous: isAnonymous }),
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
    refreshToken: 'orv_rt_x',
    refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    sessionId,
  }
}

const user = (id: string, isAnonymous: boolean): Record<string, unknown> => ({
  id,
  email: isAnonymous ? null : 'ada@example.com',
  emailVerified: false,
  name: null,
  status: 'active',
  metadata: {},
  createdAt: '2026-10-09T00:00:00Z',
  lastSignInAt: null,
  providers: [],
  hasPassword: !isAnonymous,
  mfaEnabled: false,
  isAnonymous,
})

function client(fetch: typeof globalThis.fetch, config: Partial<ClientConfig> = {}): Client {
  return new Client({ endpoint, project: 'shop', fetch, logger: quiet, maxRetries: 0, ...config })
}

function events(c: Client): AuthEvent[] {
  const seen: AuthEvent[] = []
  c.onAuthStateChange((event) => seen.push(event))
  return seen
}

const upgrade = { email: 'ada@example.com', password: 'correct horse battery' }

describe('guests (AC-36)', () => {
  it('a guest sign in stores the session and says signedIn', async () => {
    const { fetch, sent } = fakeFetch(() =>
      Response.json(
        {
          user: user('u1', true),
          session: session('u1', true),
          mfa: null,
          isNewUser: true,
          verificationEmail: null,
          verificationRequired: false,
        },
        { status: 201 },
      ),
    )
    const c = client(fetch, { session: new MemorySessionStore() })
    const seen = events(c)

    const result = await new AccountService(c).createAnonymousSession()

    expect(result.user?.isAnonymous).toBe(true)
    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/sessions/anonymous')
    expect((await c.session.get())?.sessionId).toBe('s1')
    expect(seen).toEqual(['signedIn'])
  })

  it('an upgrade that made the user permanent refreshes, so the claim says false', async () => {
    const { fetch, sent } = fakeFetch(
      () =>
        Response.json({
          user: user('u1', false),
          verificationRequired: false,
          verificationEmail: null,
        }),
      () => Response.json(session('u1', false, 's1')),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('u1', true)) })
    const seen = events(c)

    const result = await new AccountService(c).upgradeAnonymous(upgrade)

    expect(result.user?.isAnonymous).toBe(false)
    expect(sent.map((s) => new URL(s.url).pathname)).toEqual([
      '/v1/account/anonymous/upgrade',
      '/v1/account/sessions/refresh',
    ])
    expect(seen).toEqual(['tokenRefreshed', 'userUpdated'])
  })

  it('an upgrade waiting for its emailed link changes nothing here', async () => {
    const { fetch, sent } = fakeFetch(() =>
      Response.json({ user: null, verificationRequired: true, verificationEmail: null }),
    )
    const held = session('u1', true)
    const c = client(fetch, { session: new MemorySessionStore(held) })
    const seen = events(c)

    const result = await new AccountService(c).upgradeAnonymous({
      ...upgrade,
      verificationRedirectUrl: 'https://app.example.com/cb',
    })

    expect(result.verificationRequired).toBe(true)
    expect(sent).toHaveLength(1)
    expect(seen).toEqual([])
    expect(await c.session.get()).toEqual(held)
  })

  it('a link that ended the session under required MFA signs the guest out at the refresh', async () => {
    const { fetch } = fakeFetch(
      () => Response.json(user('u1', false)),
      problem(401, { code: 'invalid_refresh_token' }),
    )
    const c = client(fetch, { session: new MemorySessionStore(session('u1', true)) })
    const seen = events(c)

    await new AccountService(c).confirmEmailChange({ token: `orv_el_${'A'.repeat(43)}` })

    expect(await c.session.get()).toBeNull()
    expect(seen).toEqual(['signedOut'])
  })
})
