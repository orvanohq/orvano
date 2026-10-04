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
      open: () => `${redirectUrl}?orvano_type=oauth_link&orvano_code=${code}`,
    })

    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/account/identities/oauth/flows')
    expect(new URL(sent[1]?.url ?? '').pathname).toBe('/v1/account/identities/oauth')
    expect(result).toEqual({ type: 'oauth_link', identity: { id: 'i1', provider: 'github' } })
    expect(seen).toEqual(['userUpdated'])
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
