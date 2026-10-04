import { SignJWT, base64url, exportJWK, generateKeyPair } from 'jose'
import type { CryptoKey, JWK } from 'jose'
import { describe, expect, it, vi } from 'vitest'

import { OrvanoError } from '../src/index.js'
import { Client as ServerClient } from '../src/server.js'
import { fakeFetch, problem } from './fake-fetch.js'

// Spec 0004 AC-19: verifyAccessToken checks a user's token locally against the project's JWKS,
// keeping the keys and fetching again (with no-cache, at most once per 30 seconds) for an unknown
// kid; online also asks Orvano as the user.

const endpoint = 'https://orvano.example.com'
const project = 'shop'
const issuer = `${endpoint}/v1/projects/${project}`
const quiet = { warn: vi.fn() }

interface TestKey {
  kid: string
  privateKey: CryptoKey
  jwk: JWK
}

async function newKey(kid: string): Promise<TestKey> {
  const { privateKey, publicKey } = await generateKeyPair('ES256', { extractable: true })
  return {
    kid,
    privateKey,
    jwk: { ...(await exportJWK(publicKey)), kid, alg: 'ES256', use: 'sig' },
  }
}

async function sign(
  key: TestKey,
  claims: {
    aud?: string
    iss?: string
    sid?: string | null
    exp?: number
    emailVerified?: boolean
  } = {},
): Promise<string> {
  const exp = claims.exp ?? Math.floor(Date.now() / 1000) + 900
  const jwt = new SignJWT({
    ...(claims.sid === null ? {} : { sid: claims.sid ?? 'session-1' }),
    ...(claims.emailVerified === undefined ? {} : { email_verified: claims.emailVerified }),
  })
    .setProtectedHeader({ alg: 'ES256', typ: 'JWT', kid: key.kid })
    .setSubject('user-1')
    .setIssuer(claims.iss ?? issuer)
    .setAudience(claims.aud ?? project)
    .setIssuedAt(exp - 900)
    .setExpirationTime(exp)
  return jwt.sign(key.privateKey)
}

function jwks(...keys: TestKey[]): () => Response {
  return () =>
    new Response(JSON.stringify({ keys: keys.map((k) => k.jwk) }), {
      headers: { 'Content-Type': 'application/json' },
    })
}

function server(fetch: typeof globalThis.fetch, apiKey?: string): ServerClient {
  return new ServerClient({
    endpoint,
    project,
    fetch,
    logger: quiet,
    maxRetries: 0,
    ...(apiKey === undefined ? {} : { apiKey }),
  })
}

async function codeOf(promise: Promise<unknown>): Promise<string> {
  try {
    await promise
  } catch (error) {
    if (error instanceof OrvanoError) return `${String(error.status)} ${error.code}`
    throw error
  }
  return 'passed'
}

describe('verifyAccessToken', () => {
  it('verifies a token of the project and keeps the keys', async () => {
    const key = await newKey('k1')
    const { fetch, sent } = fakeFetch(jwks(key))
    const client = server(fetch)
    const exp = Math.floor(Date.now() / 1000) + 900

    const first = await client.verifyAccessToken(await sign(key, { exp }))
    await client.verifyAccessToken(await sign(key))

    expect(first).toEqual({
      userId: 'user-1',
      sessionId: 'session-1',
      emailVerified: false,
      expiresAt: new Date(exp * 1000),
    })
    // Spec 0010 AC-14: the email_verified claim, false when missing.
    expect(
      (await client.verifyAccessToken(await sign(key, { emailVerified: true }))).emailVerified,
    ).toBe(true)
    expect(sent).toHaveLength(1)
    expect(new URL(sent[0]?.url ?? '').pathname).toBe('/v1/projects/shop/.well-known/jwks.json')
    expect(sent[0]?.headers.has('Cache-Control')).toBe(false)
  })

  it('counts a token as token_expired after the 30 second leeway', async () => {
    const key = await newKey('k1')
    const client = server(fakeFetch(jwks(key)).fetch)
    const now = Math.floor(Date.now() / 1000)

    expect(await codeOf(client.verifyAccessToken(await sign(key, { exp: now - 20 })))).toBe(
      'passed',
    )
    expect(await codeOf(client.verifyAccessToken(await sign(key, { exp: now - 40 })))).toBe(
      '401 token_expired',
    )
  })

  it('refuses another project, issuer, algorithm, or a forgery as invalid_token', async () => {
    const key = await newKey('k1')
    const other = await newKey('k1')
    const client = server(fakeFetch(jwks(key)).fetch)
    const valid = await sign(key)
    const [header = '', body = '', signature = ''] = valid.split('.')
    const encode = (json: object): string => base64url.encode(JSON.stringify(json))
    const none = `${encode({ alg: 'none', typ: 'JWT', kid: 'k1' })}.${body}.`
    const hs256 = `${encode({ alg: 'HS256', typ: 'JWT', kid: 'k1' })}.${body}.${signature}`

    for (const forged of [
      await sign(key, { aud: 'blog' }),
      await sign(key, { iss: `${endpoint}/v1/projects/blog` }),
      await sign(key, { iss: 'https://elsewhere.example.com/v1/projects/shop' }),
      await sign(other), // same kid, other key
      await sign(key, { sid: null }),
      none,
      hs256,
      `${header}.${body}.${signature.slice(0, -4)}AAAA`,
      'not a token',
    ]) {
      expect(await codeOf(client.verifyAccessToken(forged))).toBe('401 invalid_token')
    }
  })

  it('fetches again with no-cache for an unknown kid, at most once per 30 seconds', async () => {
    const old = await newKey('old')
    const rotated = await newKey('new')
    const stranger = await newKey('other')
    const { fetch, sent } = fakeFetch(jwks(old), jwks(old, rotated))
    const client = server(fetch)

    await client.verifyAccessToken(await sign(old))
    const after = await client.verifyAccessToken(await sign(rotated))

    expect(after.userId).toBe('user-1')
    expect(await codeOf(client.verifyAccessToken(await sign(stranger)))).toBe('401 invalid_token')
    expect(sent).toHaveLength(2)
    expect(sent[1]?.headers.get('Cache-Control')).toBe('no-cache')
  })

  it('online asks Orvano as the user without the API key', async () => {
    const key = await newKey('k1')
    const user = () =>
      new Response(
        JSON.stringify({
          id: 'user-1',
          email: null,
          emailVerified: false,
          name: null,
          status: 'active',
          metadata: {},
          createdAt: '2026-01-01T00:00:00Z',
          lastSignInAt: null,
          providers: [],
          hasPassword: false,
        }),
        { headers: { 'Content-Type': 'application/json' } },
      )
    const { fetch, sent } = fakeFetch(jwks(key), user, problem(401, { code: 'invalid_token' }))
    const client = server(fetch, 'orv_sk_secret')
    const token = await sign(key)

    const verified = await client.verifyAccessToken(token, { online: true })
    const ended = await codeOf(client.verifyAccessToken(token, { online: true }))

    expect(verified.sessionId).toBe('session-1')
    const check = sent[1]
    expect(new URL(check?.url ?? '').pathname).toBe('/v1/account')
    expect(check?.headers.get('Authorization')).toBe(`Bearer ${token}`)
    expect(check?.headers.get('X-Orvano-Project')).toBe(project)
    expect(check?.headers.has('X-Orvano-Key')).toBe(false)
    expect(ended).toBe('401 invalid_token')
  })

  it('needs a project', async () => {
    const client = new ServerClient({ endpoint, logger: quiet })

    await expect(client.verifyAccessToken('x')).rejects.toThrow(/project/)
  })
})
