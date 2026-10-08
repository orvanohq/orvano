import type { PendingMfa } from './mfa.js'

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
 * `localStorage` in a browser and in memory elsewhere; `@orvano/nextjs` keeps it in cookies.
 */
export interface SessionStore {
  /** The current session, or null when nobody is signed in. */
  get(): AuthSession | null | Promise<AuthSession | null>
  /** Saves a new session, or clears it with null. */
  set(session: AuthSession | null): void | Promise<void>
  /**
   * Optional: calls `listener` when the session changes somewhere else (another tab), so the
   * client can tell its listeners. Returns a function that stops it.
   */
  subscribe?(listener: (session: AuthSession | null) => void): () => void
  /**
   * Optional: when copies of this store share one session (tabs of one origin), the name of the
   * Web Lock every copy refreshes under, so only one refreshes at a time.
   */
  readonly lockName?: string | undefined
}

/**
 * The `sub` and `email_verified` claims of an access token, read without checking it (the client
 * only compares them with a user it was just sent); null when it is not a readable JWT.
 */
export function readAccessClaims(token: string): { sub: string; emailVerified: boolean } | null {
  const payload = token.split('.')[1]
  if (payload === undefined) return null
  try {
    const padded = payload.replace(/-/g, '+').replace(/_/g, '/')
    const claims = JSON.parse(atob(padded)) as { sub?: unknown; email_verified?: unknown }
    return typeof claims.sub === 'string'
      ? { sub: claims.sub, emailVerified: claims.email_verified === true }
      : null
  } catch {
    return null
  }
}

/**
 * What happened to the signed in user, as `onAuthStateChange` tells it. `mfaRequired` (spec 0013)
 * means a sign in stopped at the MFA step: no session yet; finish it with `completeMfa`.
 */
export type AuthEvent = 'signedIn' | 'signedOut' | 'tokenRefreshed' | 'userUpdated' | 'mfaRequired'

/**
 * Called with each {@link AuthEvent} and the session after it (null after `signedOut`, and
 * unchanged after `mfaRequired`). For `mfaRequired`, `mfa` carries the factors and the expiry.
 */
export type AuthStateListener = (
  event: AuthEvent,
  session: AuthSession | null,
  mfa?: PendingMfa,
) => void

/** The `localStorage` key a browser keeps a project's session under: `orvano.session.<projectId>`. */
export function sessionStorageKey(project: string | undefined): string {
  return `orvano.session.${project ?? ''}`
}

/** The Web Lock tabs refresh a project's session under: `orvano.refresh.<projectId>`. */
export function refreshLockName(project: string | undefined): string {
  return `orvano.refresh.${project ?? ''}`
}

/** The parts of `Storage` the browser store needs. */
interface StorageLike {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

/**
 * A {@link SessionStore} in `localStorage` (spec 0004, AC-24): the default in a browser. Every tab
 * of the origin shares it; a change in one tab reaches the others through the `storage` event,
 * and tabs refresh under one Web Lock.
 */
export class LocalStorageSessionStore implements SessionStore {
  readonly #key: string
  readonly #storage: StorageLike
  readonly lockName: string

  /** A store for `project`'s session in `storage` (the page's `localStorage` by default). */
  constructor(project: string | undefined, storage?: StorageLike) {
    this.#key = sessionStorageKey(project)
    this.lockName = refreshLockName(project)
    const found = storage ?? (globalThis as { localStorage?: StorageLike }).localStorage
    if (found === undefined)
      throw new Error('Orvano: this runtime has no localStorage; pass a session store.')
    this.#storage = found
  }

  get(): AuthSession | null {
    return parseSession(this.#storage.getItem(this.#key))
  }

  set(session: AuthSession | null): void {
    if (session === null) this.#storage.removeItem(this.#key)
    else this.#storage.setItem(this.#key, JSON.stringify(session))
  }

  subscribe(listener: (session: AuthSession | null) => void): () => void {
    const target = globalThis as {
      addEventListener?: (
        type: 'storage',
        fn: (e: { key: string | null; newValue: string | null }) => void,
      ) => void
      removeEventListener?: (
        type: 'storage',
        fn: (e: { key: string | null; newValue: string | null }) => void,
      ) => void
    }
    const onStorage = (event: { key: string | null; newValue: string | null }): void => {
      if (event.key === this.#key || event.key === null) listener(parseSession(event.newValue))
    }
    target.addEventListener?.('storage', onStorage)
    return () => target.removeEventListener?.('storage', onStorage)
  }
}

/** A stored session, or null when there is none or it is not one this SDK wrote. */
function parseSession(text: string | null): AuthSession | null {
  if (text === null) return null
  try {
    const value = JSON.parse(text) as Partial<AuthSession> | null
    return typeof value?.accessToken === 'string' &&
      typeof value.accessTokenExpiresAt === 'string' &&
      typeof value.sessionId === 'string'
      ? {
          accessToken: value.accessToken,
          accessTokenExpiresAt: value.accessTokenExpiresAt,
          refreshToken: typeof value.refreshToken === 'string' ? value.refreshToken : null,
          refreshTokenExpiresAt:
            typeof value.refreshTokenExpiresAt === 'string' ? value.refreshTokenExpiresAt : null,
          sessionId: value.sessionId,
        }
      : null
  } catch {
    return null
  }
}

/** True in a browser page with `localStorage`: there the client keeps the session in it by default. */
export function hasLocalStorage(): boolean {
  const g = globalThis as { document?: unknown; localStorage?: unknown }
  try {
    return (
      typeof g.document !== 'undefined' &&
      typeof g.localStorage !== 'undefined' &&
      g.localStorage !== null
    )
  } catch {
    // Some browsers throw on localStorage access when storage is blocked.
    return false
  }
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
