import { afterEach, describe, expect, it, vi } from 'vitest'

import { Client, OrvanoError, createNonce, verifierStorageKey } from '../src/index.js'
import type { AuthEvent, AuthSession } from '../src/index.js'
import { fakeFetch } from './fake-fetch.js'

// Spec 0012 AC-20: signInWithOAuth and linkIdentity with the verifier kept for the redirect,
// redeemLink for oauth and oauth_link, the typed error an orvano_error becomes, and the nonce.

const endpoint = 'https://orvano.example.com'
const quiet = { warn: vi.fn() }
const redirectUrl = 'https://app.example.com/auth/callback'
const code = `orv_oc_${'B'.repeat(43)}`

const session: AuthSession = {
  accessToken: 'a.b.c',
  accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  refreshToken: 'orv_rt_x',
  refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
  sessionId: 's1',
}

const signedIn = (): Response =>
  Response.json(
    {
      user: { id: 'u1', providers: ['google'] },
      session,
      isNewUser: true,
      verificationEmail: null,
    },
    { status: 201 },
  )

function client(fetch: typeof globalThis.fetch): Client {
  return new Client({ endpoint, project: 'shop', fetch, logger: quiet, maxRetries: 0 })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('provider sign in (AC-20)', () => {
  it('starts with an S256 challenge, opens the URL, and redeems what open returns with the verifier', async () => {
    const { fetch, sent } = fakeFetch(
      Response.json({ url: 'https://accounts.example/auth' }),
      signedIn(),
    )
    const c = client(fetch)
    const seen: AuthEvent[] = []
    c.onAuthStateChange((event) => seen.push(event))
    const opened: string[] = []

    const result = await c.signInWithOAuth('google', {
      redirectUrl,
      open: (url) => {
        opened.push(url)
        return `${redirectUrl}?orvano_type=oauth&orvano_code=${code}`
      },
    })

    expect(opened).toEqual(['https://accounts.example/auth'])
    const start = JSON.parse(sent[0]?.body ?? '{}') as Record<string, string>
    expect(start).toMatchObject({ provider: 'google', redirectUrl })
    expect(start.codeChallenge).toMatch(/^[A-Za-z0-9_-]{43}$/)
    const redeem = JSON.parse(sent[1]?.body ?? '{}') as Record<string, string>
    expect(new URL(sent[1]?.url ?? '').pathname).toBe('/v1/account/sessions/oauth')
    expect(redeem.code).toBe(code)
    const digest = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(redeem.codeVerifier),
    )
    const challenge = btoa(String.fromCharCode(...new Uint8Array(digest)))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '')
    expect(challenge).toBe(start.codeChallenge)
    expect(result).toMatchObject({ type: 'oauth', isNewUser: true, user: { id: 'u1' } })
    expect(seen).toEqual(['signedIn'])
    expect((await c.session.get())?.sessionId).toBe('s1')
  })

  it('throws before any call outside a browser without open', async () => {
    const { fetch, sent } = fakeFetch(Response.json({ url: 'x' }))
    await expect(client(fetch).signInWithOAuth('github', { redirectUrl })).rejects.toThrow(
      TypeError,
    )
    expect(sent).toHaveLength(0)
  })

  it('keeps the verifier in sessionStorage in a browser and navigates away', async () => {
    const storage = new Map<string, string>()
    const assigned: string[] = []
    vi.stubGlobal('sessionStorage', {
      getItem: (k: string) => storage.get(k) ?? null,
      setItem: (k: string, v: string) => storage.set(k, v),
      removeItem: (k: string) => storage.delete(k),
    })
    vi.stubGlobal('location', {
      href: 'https://app.example.com/',
      assign: (url: string) => assigned.push(url),
    })
    const { fetch } = fakeFetch(Response.json({ url: 'https://accounts.example/auth' }), signedIn())
    const c = client(fetch)

    expect(await c.signInWithOAuth('apple', { redirectUrl })).toBeNull()
    expect(assigned).toEqual(['https://accounts.example/auth'])
    expect(storage.get(verifierStorageKey('shop'))).toMatch(/^[A-Za-z0-9_-]{43}$/)

    await c.redeemLink(`${redirectUrl}?orvano_type=oauth&orvano_code=${code}`)
    expect(storage.has(verifierStorageKey('shop'))).toBe(false)
  })

  it('turns orvano_error into the typed error and needs a stored verifier for a code', async () => {
    const { fetch, sent } = fakeFetch(signedIn())
    const c = client(fetch)

    const error = await c
      .redeemLink(`${redirectUrl}?orvano_type=oauth&orvano_error=oauth_access_denied`)
      .catch((e: unknown) => e)
    expect(error).toBeInstanceOf(OrvanoError)
    expect(error).toMatchObject({ status: 403, code: 'oauth_access_denied' })
    await expect(
      c.redeemLink(`${redirectUrl}?orvano_type=oauth&orvano_code=${code}`),
    ).rejects.toThrow(TypeError)
    expect(sent).toHaveLength(0)
  })

  it('links with completeOAuthLink and says userUpdated', async () => {
    const { fetch, sent } = fakeFetch(
      Response.json({ url: 'https://github.example/auth' }),
      Response.json({ id: 'i1', provider: 'github' }, { status: 201 }),
    )
    const c = client(fetch)
    const seen: AuthEvent[] = []
    c.onAuthStateChange((event) => seen.push(event))

    const result = await c.linkIdentity('github', {
      redirectUrl,
      password: 'correct horse',
      open: () => `${redirectUrl}?orvano_type=oauth_link&orvano_code=${code}`,
    })

    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/identities/oauth/flows')
    // Spec 0013: the link flow carries the user's current password; the redemption does not.
    expect(JSON.parse(sent[0]?.body ?? '{}')).toMatchObject({
      provider: 'github',
      redirectUrl,
      password: 'correct horse',
    })
    expect(new URL(sent[1]?.url ?? '').pathname).toBe('/v1/account/identities/oauth')
    expect(JSON.parse(sent[1]?.body ?? '{}')).not.toHaveProperty('password')
    expect(result).toEqual({ type: 'oauth_link', identity: { id: 'i1', provider: 'github' } })
    expect(seen).toEqual(['userUpdated'])
  })
})

describe('redeemLink for provider redirects (AC-20)', () => {
  function browser(href: string): { storage: Map<string, string>; replaced: string[] } {
    const storage = new Map<string, string>()
    const replaced: string[] = []
    vi.stubGlobal('sessionStorage', {
      getItem: (k: string) => storage.get(k) ?? null,
      setItem: (k: string, v: string) => storage.set(k, v),
      removeItem: (k: string) => storage.delete(k),
    })
    vi.stubGlobal('location', { href, assign: () => undefined })
    vi.stubGlobal('history', {
      state: null,
      replaceState: (_data: unknown, _unused: string, url?: string) => replaced.push(url ?? ''),
    })
    return { storage, replaced }
  }

  it('removes only the Orvano parameters from the address bar after a sign in', async () => {
    const { replaced } = browser(
      `${redirectUrl}?next=%2Fhome&orvano_type=oauth&orvano_code=${code}`,
    )
    const { fetch } = fakeFetch(Response.json({ url: 'https://accounts.example/auth' }), signedIn())
    const c = client(fetch)
    await c.signInWithOAuth('google', { redirectUrl })

    const result = await c.redeemLink()

    expect(result).toMatchObject({ type: 'oauth', isNewUser: true })
    expect(replaced).toEqual([`${redirectUrl}?next=%2Fhome`])
  })

  it('removes the error from the address bar too, and forgets the verifier', async () => {
    const { storage, replaced } = browser(
      `${redirectUrl}?orvano_type=oauth&orvano_error=provider_unavailable`,
    )
    const { fetch, sent } = fakeFetch(Response.json({ url: 'https://accounts.example/auth' }))
    const c = client(fetch)
    await c.signInWithOAuth('google', { redirectUrl })

    await expect(c.redeemLink()).rejects.toMatchObject({
      status: 503,
      code: 'provider_unavailable',
    })
    expect(replaced).toEqual([redirectUrl])
    expect(storage.has(verifierStorageKey('shop'))).toBe(false)
    expect(sent).toHaveLength(1)
  })

  it('leaves the address bar alone when redeeming a URL that is not the current one', async () => {
    const { replaced } = browser('https://app.example.com/')
    const { fetch } = fakeFetch(Response.json({ url: 'https://accounts.example/auth' }), signedIn())
    const c = client(fetch)
    await c.signInWithOAuth('google', { redirectUrl })

    await c.redeemLink(`${redirectUrl}?orvano_type=oauth&orvano_code=${code}`)

    expect(replaced).toEqual([])
  })

  it('throws a TypeError before any call for a provider redirect with no code', async () => {
    const { fetch, sent } = fakeFetch(signedIn())

    await expect(client(fetch).redeemLink(`${redirectUrl}?orvano_type=oauth`)).rejects.toThrow(
      TypeError,
    )
    expect(sent).toHaveLength(0)
  })

  it('uses a verifier for one redemption only', async () => {
    const { fetch, sent } = fakeFetch(
      Response.json({ url: 'https://accounts.example/auth' }),
      signedIn(),
    )
    const c = client(fetch)
    const back = `${redirectUrl}?orvano_type=oauth&orvano_code=${code}`
    await c.signInWithOAuth('google', { redirectUrl, open: () => back })

    await expect(c.redeemLink(back)).rejects.toThrow(TypeError)
    expect(sent).toHaveLength(2)
  })

  it('keeps the verifier through a network error so the same redirect can be tried again', async () => {
    const { fetch } = fakeFetch(
      Response.json({ url: 'https://accounts.example/auth' }),
      () => {
        throw new TypeError('network down')
      },
      signedIn(),
    )
    const c = client(fetch)
    const back = `${redirectUrl}?orvano_type=oauth&orvano_code=${code}`

    await expect(c.signInWithOAuth('google', { redirectUrl, open: () => back })).rejects.toThrow()
    const retried = await c.redeemLink(back)

    expect(retried).toMatchObject({ type: 'oauth' })
  })

  it('is null for a URL with no Orvano parameters', async () => {
    const { fetch, sent } = fakeFetch()

    expect(await client(fetch).redeemLink(`${redirectUrl}?next=%2F`)).toBeNull()
    expect(sent).toHaveLength(0)
  })
})

describe('native ID tokens (AC-20)', () => {
  it('signInWithIdToken sends the raw nonce and Apple code, stores the session, and says signedIn', async () => {
    const { fetch, sent } = fakeFetch(signedIn())
    const c = client(fetch)
    const seen: AuthEvent[] = []
    c.onAuthStateChange((event) => seen.push(event))
    const { raw } = await createNonce()

    const result = await c.signInWithIdToken({
      provider: 'apple',
      idToken: 'id.token.value',
      nonce: raw,
      authorizationCode: 'apple-code',
      name: 'Grace Hopper',
    })

    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/sessions/id-token')
    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({
      provider: 'apple',
      idToken: 'id.token.value',
      nonce: raw,
      authorizationCode: 'apple-code',
      name: 'Grace Hopper',
    })
    expect(result).toEqual({
      user: { id: 'u1', providers: ['google'] },
      isNewUser: true,
      mfaRequired: false,
      factors: [],
    })
    expect((await c.session.get())?.sessionId).toBe('s1')
    expect(seen).toEqual(['signedIn'])
  })

  it('leaves out the Apple only fields when they are not given', async () => {
    const { fetch, sent } = fakeFetch(signedIn())

    await client(fetch).signInWithIdToken({ provider: 'google', idToken: 't', nonce: 'n' })

    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({
      provider: 'google',
      idToken: 't',
      nonce: 'n',
    })
  })

  it('linkIdentityWithIdToken links and says userUpdated', async () => {
    const { fetch, sent } = fakeFetch(
      Response.json({ id: 'i2', provider: 'google' }, { status: 201 }),
    )
    const c = client(fetch)
    const seen: AuthEvent[] = []
    c.onAuthStateChange((event) => seen.push(event))

    const identity = await c.linkIdentityWithIdToken({
      provider: 'google',
      idToken: 't',
      nonce: 'n',
    })

    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/identities/id-token')
    expect(JSON.parse(sent[0]?.body ?? '{}')).not.toHaveProperty('password')
    expect(identity).toEqual({ id: 'i2', provider: 'google' })
    expect(seen).toEqual(['userUpdated'])
  })

  it('linkIdentityWithIdToken sends the current password with the token', async () => {
    const { fetch, sent } = fakeFetch(
      Response.json({ id: 'i2', provider: 'google' }, { status: 201 }),
    )
    const c = client(fetch)

    await c.linkIdentityWithIdToken(
      { provider: 'google', idToken: 't', nonce: 'n' },
      { password: 'correct horse' },
    )

    expect(JSON.parse(sent[0]?.body ?? '{}')).toEqual({
      provider: 'google',
      idToken: 't',
      nonce: 'n',
      password: 'correct horse',
    })
  })

  it('a refused token throws invalid_id_token and stores nothing', async () => {
    const { fetch } = fakeFetch(
      Response.json(
        { status: 401, code: 'invalid_id_token', title: 'Invalid ID token', detail: 'Refused.' },
        { status: 401, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    )
    const c = client(fetch)

    await expect(
      c.signInWithIdToken({ provider: 'google', idToken: 't', nonce: 'n' }),
    ).rejects.toMatchObject({ status: 401, code: 'invalid_id_token' })
    expect(await c.session.get()).toBeNull()
  })
})

describe('createNonce (AC-20)', () => {
  it('hashes the raw value to lowercase hex SHA-256', async () => {
    const { raw, hashed } = await createNonce()
    const digest = new Uint8Array(
      await crypto.subtle.digest('SHA-256', new TextEncoder().encode(raw)),
    )
    expect(raw).toMatch(/^[A-Za-z0-9_-]{43}$/)
    expect(hashed).toBe([...digest].map((b) => b.toString(16).padStart(2, '0')).join(''))
  })
})
