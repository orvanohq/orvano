import { describe, expect, it, vi } from 'vitest'

import {
  AccountService,
  Client,
  MemoryPendingMfaStore,
  MemorySessionStore,
  OrvanoError,
  enrollmentRequired,
} from '../src/index.js'
import type {
  AuthEvent,
  AuthSession,
  ClientConfig,
  PasskeyAuthenticator,
  PasskeyRegistrationCredential,
  PendingMfa,
} from '../src/index.js'
import { fakeFetch, problem } from './fake-fetch.js'

// Spec 0013 AC-36: a sign in that stops at the MFA step stores nothing, keeps the ticket in
// memory, says mfaRequired, and completeMfa finishes it. verifyMfa and confirmTotp store the new
// access token and keep the refresh token the client holds, since the server sends none; a store
// that hides the ticket blanks it in the result (AC-37).

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

// Spec 0013: verifyMfa and confirmTotp answer a RaisedSession, with no refresh token.
const raised = (
  sessionId: string,
  accessToken: string,
): { accessToken: string; accessTokenExpiresAt: string; sessionId: string } => ({
  accessToken,
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  sessionId,
})

const session = (sessionId: string, accessToken: string): AuthSession => ({
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
    expect(seen.mfa).toEqual([
      { factors: ['totp', 'recovery_code'], expiresAt, enrollmentRequired: false },
    ])
    expect(c.pendingMfa).toEqual({
      factors: ['totp', 'recovery_code'],
      expiresAt,
      enrollmentRequired: false,
    })
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
      enrollmentRequired: false,
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

describe('step up and turning MFA on (AC-19, AC-13, AC-36)', () => {
  it('verifyMfa sends the factor as the user, stores the new access token, keeps the refresh token, and says tokenRefreshed', async () => {
    const answer = raised('s1', 'stronger')
    const { fetch, sent } = fakeFetch(() => Response.json(answer))
    const sessions = new MemorySessionStore()
    const before = session('s1', 'weaker')
    sessions.set(before)
    const c = client(fetch, { session: sessions })
    const seen = listen(c)

    await c.verifyMfa({ recoveryCode: 'AAAAA-BBBBB' })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/mfa/verify`)
    expect(sent[0]?.headers.get('Authorization')).toBe('Bearer weaker')
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ recoveryCode: 'AAAAA-BBBBB' })
    expect(await c.session.get()).toEqual({
      accessToken: 'stronger',
      accessTokenExpiresAt: answer.accessTokenExpiresAt,
      refreshToken: before.refreshToken,
      refreshTokenExpiresAt: before.refreshTokenExpiresAt,
      sessionId: 's1',
    })
    expect(seen.events).toEqual(['tokenRefreshed'])
  })

  it('confirmTotp answers the recovery codes, stores the new access token, and keeps the refresh token', async () => {
    const codes = ['AAAAA-BBBBB', 'CCCCC-DDDDD']
    const { fetch, sent } = fakeFetch(() =>
      Response.json({ recoveryCodes: codes, session: raised('s1', 'level2') }),
    )
    const sessions = new MemorySessionStore()
    const before = session('s1', 'level1')
    sessions.set(before)
    const c = client(fetch, { session: sessions })
    const seen = listen(c)

    expect(await c.confirmTotp('123456')).toEqual(codes)
    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/mfa/totp/confirm`)
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ code: '123456' })
    const after = await c.session.get()
    expect(after?.accessToken).toBe('level2')
    expect(after?.refreshToken).toBe(before.refreshToken)
    expect(after?.refreshTokenExpiresAt).toBe(before.refreshTokenExpiresAt)
    expect(seen.events).toEqual(['tokenRefreshed'])
  })

  it('createTotp sends the current password in its body', async () => {
    const { fetch, sent } = fakeFetch(() =>
      Response.json({ secret: 'S', uri: 'otpauth://totp/x', expiresAt }, { status: 201 }),
    )
    const sessions = new MemorySessionStore()
    sessions.set(session('s1', 'level1'))
    const c = client(fetch, { session: sessions })

    await new AccountService(c).createTotp({ password: 'correct horse' })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/mfa/totp`)
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({ password: 'correct horse' })
  })

  it('goes through a transport when one is set, and announceMfa says mfaRequired', async () => {
    const { fetch, sent } = fakeFetch(signedIn)
    const transport = {
      completeMfa: vi.fn(() =>
        Promise.resolve({ user: null, isNewUser: false, mfaRequired: false, factors: [] }),
      ),
      verifyMfa: vi.fn(() => Promise.resolve()),
      confirmTotp: vi.fn(() => Promise.resolve(['AAAAA-BBBBB'])),
    }
    const c = client(fetch, { mfa: transport })
    const seen = listen(c)

    c.announceMfa({ factors: ['totp'], expiresAt, enrollmentRequired: false })
    await c.completeMfa({ totpCode: '123456' })
    await c.verifyMfa({ totpCode: '123456' })
    expect(await c.confirmTotp('123456')).toEqual(['AAAAA-BBBBB'])

    expect(sent).toHaveLength(0)
    expect(seen.events).toEqual(['mfaRequired'])
    expect(c.pendingMfa).toEqual({ factors: ['totp'], expiresAt, enrollmentRequired: false })
    expect(transport.completeMfa).toHaveBeenCalledWith({ totpCode: '123456' }, c, undefined)
  })

  it('blanks the ticket in the result when the store hides it, and keeps it in the store', async () => {
    const { fetch } = fakeFetch(challenged)
    const store = new (class extends MemoryPendingMfaStore {
      readonly hidesTicket = true
    })()
    const c = client(fetch, { mfaStore: store })

    const result = await new AccountService(c).createPasswordSession({
      email: 'ada@example.com',
      password: 'pw',
    })

    expect(result.mfa?.ticket).toBe('')
    expect(store.get()?.ticket).toBe(ticket)
  })
})

// Spec 0014 AC-27, AC-36: a project that requires MFA answers a user with no factor an enrollment
// challenge; the enrollment helpers spend its ticket and store the session the first factor earns.
describe('required MFA enrollment (spec 0014)', () => {
  const enroll = (): Response =>
    Response.json(
      {
        user: null,
        session: null,
        mfa: { ticket, factors: ['totp', 'passkey'], expiresAt, enrollmentRequired: true },
        isNewUser: false,
        verificationEmail: null,
        verificationRequired: false,
      },
      { status: 201 },
    )
  const enrolled = (recoveryCodes: string[] | null): Response =>
    Response.json(
      {
        auth: {
          user: { id: 'u1', email: 'ada@example.com' },
          session: tokens('s9', 'strong'),
          mfa: null,
          isNewUser: false,
          verificationEmail: null,
          verificationRequired: false,
        },
        recoveryCodes,
      },
      { status: 201 },
    )
  const setup = (): Response =>
    Response.json(
      { secret: 'JBSWY3DPEHPK3PXP', uri: 'otpauth://totp/x', expiresAt },
      { status: 201 },
    )

  it('stops at enrollment, refuses completeMfa, and enrolls TOTP with the ticket', async () => {
    const { fetch, sent } = fakeFetch(enroll, setup, () => enrolled(['AAAAA-BBBBB']))
    const c = client(fetch)
    const seen = listen(c)

    const result = await new AccountService(c).createPasswordSession({
      email: 'ada@example.com',
      password: 'pw',
    })
    expect(enrollmentRequired(result)).toBe(true)
    expect(await c.session.get()).toBeNull()
    expect(c.pendingMfa).toEqual({
      factors: ['totp', 'passkey'],
      expiresAt,
      enrollmentRequired: true,
    })
    await expect(c.completeMfa({ totpCode: '123456' })).rejects.toThrow(TypeError)

    expect((await c.startTotpEnrollment()).secret).toBe('JBSWY3DPEHPK3PXP')
    const outcome = await c.completeTotpEnrollment('123456')

    expect(outcome).toEqual({
      user: { id: 'u1', email: 'ada@example.com' },
      recoveryCodes: ['AAAAA-BBBBB'],
    })
    expect(sent.slice(1).map((r) => r.url.replace(endpoint, ''))).toEqual([
      '/v1/account/mfa/enrollment/totp',
      '/v1/account/mfa/enrollment/totp/confirm',
    ])
    expect(JSON.parse(sent[2]?.body ?? '{}')).toEqual({ ticket, code: '123456' })
    expect((await c.session.get())?.accessToken).toBe('strong')
    expect(c.pendingMfa).toBeNull()
    expect(seen.events).toEqual(['mfaRequired', 'signedIn'])
  })

  it('enrolls a passkey through the authenticator and forgets an ended ticket', async () => {
    const credential = { id: 'cred' } as unknown as PasskeyRegistrationCredential
    const passkeys: PasskeyAuthenticator = {
      isSupported: () => Promise.resolve(true),
      create: () => Promise.resolve(credential),
      get: () => Promise.reject(new Error('not asked')),
    }
    const registration = (): Response =>
      Response.json({ challengeId: 'ch1', options: { challenge: 'x' } }, { status: 201 })
    const { fetch, sent } = fakeFetch(enroll, registration, () => enrolled(null))
    const c = client(fetch, { passkeys })

    await new AccountService(c).createPasswordSession({ email: 'ada@example.com', password: 'pw' })
    expect(await c.enrollPasskey({ name: 'Laptop' })).toEqual({
      user: { id: 'u1', email: 'ada@example.com' },
      recoveryCodes: null,
    })
    expect(JSON.parse(sent[2]?.body ?? '{}')).toEqual({
      ticket,
      challengeId: 'ch1',
      credential,
      name: 'Laptop',
    })
    expect((await c.session.get())?.sessionId).toBe('s9')

    const ended = fakeFetch(enroll, problem(401, { code: 'invalid_mfa_ticket', status: 401 }))
    const again = client(ended.fetch)
    await new AccountService(again).createPasswordSession({ email: 'a@x.com', password: 'pw' })
    await expect(again.startTotpEnrollment()).rejects.toThrow(OrvanoError)
    expect(again.pendingMfa).toBeNull()
  })
})
