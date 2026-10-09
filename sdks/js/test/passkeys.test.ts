import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  AccountService,
  Client,
  MemorySessionStore,
  browserPasskeys,
  fromBase64Url,
  toBase64Url,
} from '../src/index.js'
import type {
  AuthEvent,
  ClientConfig,
  PasskeyAssertionCredential,
  PasskeyAuthenticator,
  PasskeyGetRequest,
  PasskeyRegistrationCredential,
  PasskeyRequestOptions,
} from '../src/index.js'
import { fakeFetch } from './fake-fetch.js'

// Spec 0013 AC-36: signInWithPasskey, registerPasskey, and { passkey: true } for completeMfa and
// verifyMfa run the challenge, the client's passkey authenticator, then the answer; the browser
// authenticator uses the native JSON helpers when they exist and a base64url fallback otherwise.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }
const ticket = `orv_mt_${'A'.repeat(43)}`
const requestOptions: PasskeyRequestOptions = {
  challenge: 'Y2hhbGxlbmdl',
  rpId: 'example.com',
  timeout: 300000,
  userVerification: 'required',
  allowCredentials: [],
}
const assertion: PasskeyAssertionCredential = {
  id: 'cred',
  rawId: 'cred',
  type: 'public-key',
  response: { clientDataJSON: 'cd', authenticatorData: 'ad', signature: 'sig', userHandle: 'uh' },
  authenticatorAttachment: 'platform',
}
const registration: PasskeyRegistrationCredential = {
  id: 'cred',
  rawId: 'cred',
  type: 'public-key',
  response: { clientDataJSON: 'cd', attestationObject: 'ao', transports: ['internal'] },
  authenticatorAttachment: 'platform',
}

const tokens = (sessionId: string): Record<string, string> => ({
  accessToken: 'access',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: `orv_rt_${sessionId}`,
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  sessionId,
})
const challenge = (): Response => Response.json({ challengeId: 'ch1', options: requestOptions })
const signedIn = (): Response =>
  Response.json(
    {
      user: { id: 'u1', email: 'ada@example.com' },
      session: tokens('s2'),
      mfa: null,
      isNewUser: false,
      verificationEmail: null,
    },
    { status: 201 },
  )
const challenged = (): Response =>
  Response.json(
    {
      user: null,
      session: null,
      mfa: { ticket, factors: ['totp', 'passkey'], expiresAt: '2026-10-07T12:05:00Z' },
      isNewUser: false,
      verificationEmail: null,
    },
    { status: 201 },
  )

/** A passkey authenticator that answers fixed credentials and records what it was asked. */
function fakePasskeys(): PasskeyAuthenticator & {
  gets: [PasskeyRequestOptions, PasskeyGetRequest | undefined][]
} {
  const gets: [PasskeyRequestOptions, PasskeyGetRequest | undefined][] = []
  return {
    gets,
    isSupported: () => Promise.resolve(true),
    create: () => Promise.resolve(registration),
    get: (options, request) => {
      gets.push([options, request])
      return Promise.resolve(assertion)
    },
  }
}

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

const bodyOf = (body: string | undefined): unknown => JSON.parse(body ?? '{}')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('passkey sign in and registration (AC-36)', () => {
  it('signInWithPasskey takes a challenge, asks the authenticator, sends its answer, and stores the session', async () => {
    const { fetch, sent } = fakeFetch(challenge, signedIn)
    const passkeys = fakePasskeys()
    const c = client(fetch, { passkeys })
    const events: AuthEvent[] = []
    c.onAuthStateChange((event) => events.push(event))

    const result = await c.signInWithPasskey({ autofill: true })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/sessions/passkey-challenge`)
    expect(passkeys.gets[0]).toEqual([requestOptions, { mediation: 'conditional' }])
    expect(sent[1]?.url).toBe(`${endpoint}/v1/account/sessions/passkey`)
    expect(bodyOf(sent[1]?.body)).toEqual({ challengeId: 'ch1', credential: assertion })
    expect(result).toMatchObject({ user: { id: 'u1' }, mfaRequired: false })
    expect((await c.session.get())?.sessionId).toBe('s2')
    expect(events).toEqual(['signedIn'])
  })

  it('registerPasskey sends the new passkey with its name', async () => {
    const { fetch, sent } = fakeFetch(
      () =>
        Response.json({
          challengeId: 'reg1',
          options: { rp: { id: 'example.com', name: 'Acme' } },
        }),
      () => Response.json({ id: 'p1', name: 'Laptop' }, { status: 201 }),
    )
    const c = client(fetch, { passkeys: fakePasskeys() })

    const passkey = await c.registerPasskey({ name: 'Laptop' })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/passkeys/registration`)
    expect(bodyOf(sent[0]?.body)).toEqual({})
    expect(sent[1]?.url).toBe(`${endpoint}/v1/account/passkeys`)
    expect(bodyOf(sent[1]?.body)).toEqual({
      challengeId: 'reg1',
      credential: registration,
      name: 'Laptop',
    })
    expect(passkey).toEqual({ id: 'p1', name: 'Laptop' })
  })

  it('registerPasskey sends the current password to start the registration', async () => {
    const { fetch, sent } = fakeFetch(
      () =>
        Response.json({
          challengeId: 'reg1',
          options: { rp: { id: 'example.com', name: 'Acme' } },
        }),
      () => Response.json({ id: 'p1', name: 'Passkey' }, { status: 201 }),
    )
    const c = client(fetch, { passkeys: fakePasskeys() })

    await c.registerPasskey({ password: 'correct horse' })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/passkeys/registration`)
    expect(bodyOf(sent[0]?.body)).toEqual({ password: 'correct horse' })
    expect(bodyOf(sent[1]?.body)).not.toHaveProperty('password')
  })

  it('completeMfa with a passkey asks for the ticket challenge and answers with the passkey', async () => {
    const { fetch, sent } = fakeFetch(challenged, challenge, signedIn)
    const c = client(fetch, { passkeys: fakePasskeys() })
    await new AccountService(c).createPasswordSession({ email: 'ada@example.com', password: 'pw' })

    await c.completeMfa({ passkey: true })

    expect(sent[1]?.url).toBe(`${endpoint}/v1/account/sessions/mfa/passkey-challenge`)
    expect(bodyOf(sent[1]?.body)).toEqual({ ticket })
    expect(bodyOf(sent[2]?.body)).toEqual({
      ticket,
      passkey: { challengeId: 'ch1', credential: assertion },
    })
    expect(c.pendingMfa).toBeNull()
  })

  it('verifyMfa with a passkey steps up through the step up challenge', async () => {
    const { fetch, sent } = fakeFetch(challenge, () =>
      Response.json({
        accessToken: 'access',
        accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
        sessionId: 's1',
      }),
    )
    const c = client(fetch, { passkeys: fakePasskeys() })
    await c.session.set({ ...tokens('s1'), accessToken: 'old' })

    await c.verifyMfa({ passkey: true })

    expect(sent[0]?.url).toBe(`${endpoint}/v1/account/mfa/passkey-challenge`)
    expect(sent[0]?.headers.get('Authorization')).toBe('Bearer old')
    expect(bodyOf(sent[1]?.body)).toEqual({
      passkey: { challengeId: 'ch1', credential: assertion },
    })
    expect((await c.session.get())?.accessToken).toBe('access')
    expect((await c.session.get())?.refreshToken).toBe('orv_rt_s1')
  })
})

describe('the browser passkey authenticator (AC-36)', () => {
  it('reports no passkeys where WebAuthn is missing', async () => {
    vi.stubGlobal('PublicKeyCredential', undefined)
    expect(await browserPasskeys.isSupported()).toBe(false)
  })

  it('checks conditional mediation for autofill', async () => {
    vi.stubGlobal('PublicKeyCredential', {
      isConditionalMediationAvailable: () => Promise.resolve(false),
    })
    vi.stubGlobal('navigator', { credentials: {} })
    expect(await browserPasskeys.isSupported()).toBe(true)
    expect(await browserPasskeys.isSupported({ autofill: true })).toBe(false)
  })

  it('falls back to base64url by hand when the native JSON helpers are missing', async () => {
    const get = vi.fn(() =>
      Promise.resolve({
        id: 'AQID',
        type: 'public-key',
        rawId: new Uint8Array([1, 2, 3]).buffer,
        authenticatorAttachment: null,
        response: {
          clientDataJSON: new Uint8Array([4]).buffer,
          authenticatorData: new Uint8Array([5]).buffer,
          signature: new Uint8Array([6]).buffer,
          userHandle: null,
        },
      }),
    )
    vi.stubGlobal('PublicKeyCredential', {})
    vi.stubGlobal('navigator', { credentials: { get } })

    const answer = await browserPasskeys.get({
      ...requestOptions,
      allowCredentials: [{ type: 'public-key', id: 'AQID' }],
    })

    const call = get.mock.calls[0] as unknown as [{ publicKey: PublicKeyCredentialRequestOptions }]
    expect(new Uint8Array(call[0].publicKey.challenge as ArrayBuffer)).toEqual(
      fromBase64Url('Y2hhbGxlbmdl'),
    )
    expect(answer).toEqual({
      id: 'AQID',
      rawId: 'AQID',
      type: 'public-key',
      response: {
        clientDataJSON: 'BA',
        authenticatorData: 'BQ',
        signature: 'Bg',
        userHandle: null,
      },
      authenticatorAttachment: null,
    })
  })

  it('round trips base64url without padding', () => {
    const bytes = new Uint8Array([251, 255, 0, 1])
    expect(toBase64Url(bytes)).toBe('-_8AAQ')
    expect(fromBase64Url('-_8AAQ')).toEqual(bytes)
    expect(fromBase64Url('-_8AAQ==')).toEqual(bytes)
  })
})
