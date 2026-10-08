import { describe, expect, it, vi } from 'vitest'

import { AccountService, Client, MemorySessionStore, OrvanoError } from '../src/index.js'
import type { AuthEvent, ClientConfig, PendingMfa } from '../src/index.js'
import { fakeFetch, problem } from './fake-fetch.js'

// Spec 0013 AC-36: a sign in that stops at the MFA step stores nothing, keeps the ticket in
// memory, says mfaRequired, and completeMfa finishes it.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }
const ticket = `orv_mt_${'A'.repeat(43)}`
const expiresAt = '2026-10-07T12:05:00Z'

const tokens = (sessionId: string, accessToken = 'access'): Record<string, string> => ({
  accessToken,
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: `orv_rt_${sessionId}`,
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  sessionId,
})

const challenged = (): Response =>
  Response.json(
    {
      user: null,
      session: null,
      mfa: { ticket, factors: ['totp', 'recovery_code'], expiresAt },
      isNewUser: false,
      verificationEmail: null,
    },
    { status: 201 },
  )

const signedIn = (sessionId = 's2'): Response =>
  Response.json(
    {
      user: { id: 'u1', email: 'ada@example.com' },
      session: tokens(sessionId),
      mfa: null,
      isNewUser: false,
      verificationEmail: null,
    },
    { status: 201 },
  )

function client(fetch: typeof globalThis.fetch, config: Partial<ClientConfig> = {}): Client {
  return new Client({
    endpoint,
    project: 'shop',
    fetch,
    logger: quiet,
    maxRetries: 0,
    session: new MemorySessionStore(),
    ...config,
  })
}

function listen(c: Client): { events: AuthEvent[]; mfa: (PendingMfa | undefined)[] } {
  const seen = { events: [] as AuthEvent[], mfa: [] as (PendingMfa | undefined)[] }
  c.onAuthStateChange((event, _session, mfa) => {
    seen.events.push(event)
    seen.mfa.push(mfa)
  })
  return seen
}

describe('the MFA step (AC-36)', () => {
  it('stores nothing at step one, says mfaRequired with the factors, and keeps the ticket private', async () => {
    const { fetch } = fakeFetch(challenged)
    const c = client(fetch)
    const seen = listen(c)

    const result = await new AccountService(c).createPasswordSession({
      email: 'ada@example.com',
      password: 'pw',
    })

    expect(result.mfa?.factors).toEqual(['totp', 'recovery_code'])
    expect(await c.session.get()).toBeNull()
    expect(seen.events).toEqual(['mfaRequired'])
    expect(seen.mfa).toEqual([{ factors: ['totp', 'recovery_code'], expiresAt }])
    expect(c.pendingMfa).toEqual({ factors: ['totp', 'recovery_code'], expiresAt })
    expect(JSON.stringify(c.pendingMfa)).not.toContain(ticket)
  })

  it('completeMfa sends the ticket and the code, stores the session, and says signedIn', async () => {
    const { fetch, sent } = fakeFetch(challenged, signedIn)
    const c = client(fetch)
    const seen = listen(c)
    await new AccountService(c).createPasswordSession({ email: 'ada@example.com', password: 'pw' })

    const result = await c.completeMfa({ totpCode: '123456' })

    expect(sent[1]?.url).toBe(`${endpoint}/v1/account/sessions/mfa`)
    expect(JSON.parse(sent[1]?.body ?? '{}')).toEqual({ ticket, totpCode: '123456' })
    expect(sent[1]?.headers.get('Authorization')).toBeNull()
    expect(result).toEqual({
      user: { id: 'u1', email: 'ada@example.com' },
      isNewUser: false,
      mfaRequired: false,
      factors: [],
    })
    expect((await c.session.get())?.sessionId).toBe('s2')
    expect(seen.events).toEqual(['mfaRequired', 'signedIn'])
    expect(c.pendingMfa).toBeNull()
  })

  it('keeps the ticket after a wrong code and forgets it once the ticket has ended', async () => {
    const { fetch } = fakeFetch(
      challenged,
      problem(401, { code: 'invalid_mfa_code', status: 401 }),
      problem(401, { code: 'invalid_mfa_ticket', status: 401 }),
    )
    const c = client(fetch)
    await new AccountService(c).createPasswordSession({ email: 'ada@example.com', password: 'pw' })

    await expect(c.completeMfa({ recoveryCode: 'AAAAA-BBBBB' })).rejects.toMatchObject({
      code: 'invalid_mfa_code',
    })
    expect(c.pendingMfa).not.toBeNull()
    await expect(c.completeMfa({ totpCode: '000000' })).rejects.toBeInstanceOf(OrvanoError)
    expect(c.pendingMfa).toBeNull()
    await expect(c.completeMfa({ totpCode: '000000' })).rejects.toThrow(TypeError)
  })

  it('throws before any call when no sign in waits for MFA', async () => {
    const { fetch, sent } = fakeFetch(signedIn)
    const c = client(fetch)

    await expect(c.completeMfa({ totpCode: '123456' })).rejects.toThrow(TypeError)
    expect(sent).toHaveLength(0)
  })
})
