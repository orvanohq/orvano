/**
 * A signed in user's session, as a client keeps it: what sign up, sign in, and refresh return
 * (spec 0004). Times are ISO 8601 strings, as on the wire.
 */
export interface AuthSession {
  /** The access token, an ES256 JWT sent as `Authorization: Bearer`. */
  accessToken: string
  /** When the access token expires. */
  accessTokenExpiresAt: string
  /**
   * Trades itself for a new pair once. Null where this runtime can't read it (in a Next.js
   * browser client it lives in an `HttpOnly` cookie).
   */
  refreshToken: string | null
  /** When the session ends unless it refreshes first; null when unknown to this runtime. */
  refreshTokenExpiresAt: string | null
  /** The session ID, also the access token's `sid` claim. */
  sessionId: string
}

/**
 * Where a client keeps the signed in user's session between calls. The default keeps it in
 * memory; `@orvano/nextjs` keeps it in cookies.
 */
export interface SessionStore {
  /** The current session, or null when nobody is signed in. */
  get(): AuthSession | null | Promise<AuthSession | null>
  /** Saves a new session, or clears it with null. */
  set(session: AuthSession | null): void | Promise<void>
}

/** A {@link SessionStore} that lives as long as the client: the default. */
export class MemorySessionStore implements SessionStore {
  #session: AuthSession | null

  constructor(session: AuthSession | null = null) {
    this.#session = session
  }

  get(): AuthSession | null {
    return this.#session
  }

  set(session: AuthSession | null): void {
    this.#session = session
  }
}

/** The header the access token travels in, as `Bearer <token>`; named only here in the TS runtime. */
export const authorizationHeader = 'Authorization'

/**
 * Reads a session out of a response body: the `SessionTokens` a sign in or refresh returns.
 * Throws when a field is missing, which would mean the server broke the contract.
 */
export function sessionFrom(value: unknown): AuthSession {
  const tokens = value as Partial<Record<keyof AuthSession, unknown>> | null
  const text = (key: keyof AuthSession): string => {
    const field = tokens?.[key]
    if (typeof field !== 'string' || field === '')
      throw new TypeError(`Orvano: the session in the response has no ${key}`)
    return field
  }
  return {
    accessToken: text('accessToken'),
    accessTokenExpiresAt: text('accessTokenExpiresAt'),
    refreshToken: text('refreshToken'),
    refreshTokenExpiresAt: text('refreshTokenExpiresAt'),
    sessionId: text('sessionId'),
  }
}
