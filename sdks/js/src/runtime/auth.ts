/**
 * Where a client keeps the signed in user's session token between calls. The default keeps it in
 * memory; `@orvano/nextjs` keeps it in a cookie.
 */
export interface SessionStore {
  /** The current token, or null when nobody is signed in. */
  get(): string | null | Promise<string | null>
  /** Saves a new token, or clears it with null. */
  set(token: string | null): void | Promise<void>
}

/** A {@link SessionStore} that lives as long as the client: the default. */
export class MemorySessionStore implements SessionStore {
  #token: string | null

  constructor(token: string | null = null) {
    this.#token = token
  }

  get(): string | null {
    return this.#token
  }

  set(token: string | null): void {
    this.#token = token
  }
}

/**
 * The header an app session travels in. Temporary: the auth spec (scope row 8) replaces it, and
 * this is the only place the TS runtime names it.
 */
export const sessionHeader = 'X-Orvano-Session'
