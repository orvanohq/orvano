import { createLocalJWKSet, decodeProtectedHeader, errors, jwtVerify } from 'jose'
import type { JSONWebKeySet, JWTPayload } from 'jose'
import type { Client } from './client.js'
import { OrvanoError } from './error.js'

/** A user's access token that checked out: who, which session, and until when. */
export interface VerifiedAccessToken {
  /** The user ID (the `sub` claim). */
  userId: string
  /** The session ID (the `sid` claim). */
  sessionId: string
  /** When the token expires (the `exp` claim). */
  expiresAt: Date
}

/** Options for {@link AccessTokenVerifier.verify}. */
export interface VerifyAccessTokenOptions {
  /** Also ask Orvano, as the user and never with the API key, whether the session is still active. */
  online?: boolean
  /** Cancels the check. */
  signal?: AbortSignal
}

/** How long the project's signing keys are kept before they are fetched again. */
export const keysLifetimeMs = 10 * 60_000
/** After a token names an unknown key, the keys are fetched again at most this often. */
export const keysRefetchIntervalMs = 30_000
/** The clock skew allowed on `exp`, in seconds. */
export const clockLeewaySeconds = 30

/** Checks access tokens against one project's JWKS (spec 0004, AC-19). */
export class AccessTokenVerifier {
  readonly #client: Client
  readonly #now: () => number
  #keys: JSONWebKeySet | undefined
  #loadedAt = 0
  #refetchedAt = Number.NEGATIVE_INFINITY
  #loading: Promise<void> | undefined

  /** A verifier that fetches through `client`, which must name a project. `now` is for tests. */
  constructor(client: Client, now: () => number = Date.now) {
    this.#client = client
    this.#now = now
  }

  /** See the server `Client.verifyAccessToken`. */
  async verify(
    token: string,
    options: VerifyAccessTokenOptions = {},
  ): Promise<VerifiedAccessToken> {
    const project = this.#client.project
    if (project === undefined) {
      throw new Error('Give the client a project to verify its access tokens.')
    }

    let kid: string | undefined
    try {
      const header = decodeProtectedHeader(token)
      if (header.alg !== 'ES256') throw invalid('The access token is not an ES256 JWT of Orvano.')
      kid = header.kid
    } catch (error) {
      if (error instanceof OrvanoError) throw error
      throw invalid('The access token is not a JWT.')
    }
    if (kid === undefined) throw invalid('The access token names no key.')

    const keys = await this.#keysFor(project, kid, options.signal)
    let payload: JWTPayload
    try {
      ;({ payload } = await jwtVerify(token, createLocalJWKSet(keys), {
        issuer: `${this.#client.endpoint}/v1/projects/${project}`,
        audience: project,
        algorithms: ['ES256'],
        clockTolerance: clockLeewaySeconds,
        currentDate: new Date(this.#now()),
        requiredClaims: ['sub', 'sid', 'exp'],
      }))
    } catch (error) {
      if (error instanceof errors.JWTExpired) {
        throw new OrvanoError(
          401,
          'token_expired',
          'The access token has expired; refresh the session.',
          null,
        )
      }
      throw invalid('The access token is not valid for this project.')
    }

    const { sub, sid, exp } = payload as { sub?: unknown; sid?: unknown; exp?: unknown }
    if (
      typeof sub !== 'string' ||
      sub === '' ||
      typeof sid !== 'string' ||
      sid === '' ||
      typeof exp !== 'number'
    ) {
      throw invalid('The access token names no user, session, or expiry.')
    }

    if (options.online === true) {
      await this.#client.request<unknown>(
        { method: 'GET', path: '/v1/account', bearer: token },
        options.signal === undefined ? undefined : { signal: options.signal },
      )
    }
    return { userId: sub, sessionId: sid, expiresAt: new Date(exp * 1000) }
  }

  /**
   * The keys, fetched when older than {@link keysLifetimeMs}, and again with
   * `Cache-Control: no-cache` (at most once per {@link keysRefetchIntervalMs}) when `kid` is not
   * among them, so no cache in between serves the keys from before a rotation.
   */
  async #keysFor(
    project: string,
    kid: string,
    signal: AbortSignal | undefined,
  ): Promise<JSONWebKeySet> {
    const now = this.#now()
    if (this.#keys === undefined || now - this.#loadedAt >= keysLifetimeMs) {
      await this.#load(project, false, signal)
    } else if (
      !this.#keys.keys.some((k) => k.kid === kid) &&
      now - this.#refetchedAt >= keysRefetchIntervalMs
    ) {
      this.#refetchedAt = now
      await this.#load(project, true, signal)
    }
    return this.#keys ?? { keys: [] }
  }

  /** Fetches the JWKS, sharing one fetch between concurrent callers. */
  #load(project: string, noCache: boolean, signal: AbortSignal | undefined): Promise<void> {
    this.#loading ??= (async () => {
      try {
        const jwks = await this.#client.request<JSONWebKeySet>(
          {
            method: 'GET',
            path: `/v1/projects/${encodeURIComponent(project)}/.well-known/jwks.json`,
            noCache,
          },
          signal === undefined ? undefined : { signal },
        )
        this.#keys = { keys: jwks.keys.filter((k) => k.kty === 'EC' && k.crv === 'P-256') }
        this.#loadedAt = this.#now()
      } finally {
        this.#loading = undefined
      }
    })()
    return this.#loading
  }
}

function invalid(message: string): OrvanoError {
  return new OrvanoError(401, 'invalid_token', message, null)
}
