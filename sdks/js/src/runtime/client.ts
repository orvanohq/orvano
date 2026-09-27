import {
  LocalStorageSessionStore,
  MemorySessionStore,
  authorizationHeader,
  hasLocalStorage,
  sessionFrom,
} from './auth.js'
import type { AuthEvent, AuthSession, AuthStateListener, SessionStore } from './auth.js'
import { OrvanoError } from './error.js'
import { sdkHeader, sdkName, serverVersionHeader, versionMismatch } from './version.js'
import type { Logger } from './version.js'
import { sdkVersion } from '../generated/version.js'

/** Settings for a {@link Client}. */
export interface ClientConfig {
  /** Your Orvano server's base URL, for example `https://orvano.example.com`. */
  endpoint: string
  /** The project ID, sent as `X-Orvano-Project` on every call. */
  project?: string
  /** Extra headers sent with every request. */
  headers?: Record<string, string>
  /**
   * Where the signed in user's session lives. Defaults to `localStorage` under
   * `orvano.session.<project>` in a browser, and to memory elsewhere.
   */
  session?: SessionStore
  /**
   * How the client trades the session for a fresh one. Defaults to `account.refreshSession` with
   * the stored refresh token; `@orvano/nextjs` asks the app's route handler instead.
   */
  refresh?: SessionRefresher
  /** How long one call may take, retries included, in milliseconds. Defaults to 30000; 0 turns it off. */
  timeoutMs?: number
  /** How many times a safe call is retried after a 429 or 503. Defaults to 3. */
  maxRetries?: number
  /** A custom `fetch`, for tests or runtimes without a global one. */
  fetch?: typeof fetch
  /**
   * Where warnings go, for example the one logged when the server's major.minor differs from
   * this SDK's. Defaults to `console`.
   */
  logger?: Logger
}

/** Per call settings, the last argument of every generated method. */
export interface RequestOptions {
  /** Cancels the call. */
  signal?: AbortSignal
  /** Overrides the client's timeout for this call, in milliseconds; 0 turns it off. */
  timeoutMs?: number
}

/** The HTTP methods the contract uses. */
export type HttpMethod = 'GET' | 'HEAD' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** One HTTP call, as a generated service describes it. */
export interface RequestSpec {
  /** The HTTP method. */
  method: HttpMethod
  /** The path under the endpoint, starting with `/v1/`. */
  path: string
  /** Query parameters; `undefined` values are left out. */
  query?: Record<string, string | number | boolean | undefined> | undefined
  /** The JSON body. */
  body?: unknown
  /** Marked `x-orvano-idempotent` in the contract, so it is safe to retry. */
  idempotent?: boolean
  /**
   * Marked `x-orvano-session` in the contract: on success, `start` stores the response's
   * `session`, `refresh` stores the response itself, `end` clears the stored session, and `user`
   * tells the listeners the user changed.
   */
  session?: 'start' | 'refresh' | 'end' | 'user'
  /** Sends no user credentials and never refreshes first: the refresh call itself. */
  anonymous?: boolean
  /**
   * An access token to send as `Authorization: Bearer` instead of this client's own credentials,
   * so a call made as that user never carries an API key.
   */
  bearer?: string
  /** Sends `Cache-Control: no-cache`, so no cache in between answers from before a change. */
  noCache?: boolean
}

/**
 * How a client trades a session for a fresh one. Throws an {@link OrvanoError} with status 401
 * when the session is over; any other error (a network error, a timeout) keeps the session.
 */
export type SessionRefresher = (session: AuthSession, client: Client) => Promise<AuthSession>

/** A client refreshes before a call when less than this is left of the access token (AC-26). */
export const refreshMarginMs = 60_000

/** The default {@link SessionRefresher}: `account.refreshSession` with the stored refresh token. */
export const refreshWithToken: SessionRefresher = async (session, client) => {
  if (session.refreshToken === null)
    throw new OrvanoError(401, 'invalid_refresh_token', 'This client holds no refresh token.', null)
  const tokens = await client.request<unknown>({
    method: 'POST',
    path: '/v1/account/sessions/refresh',
    body: { refreshToken: session.refreshToken },
    idempotent: true,
    anonymous: true,
  })
  return sessionFrom(tokens)
}

const defaultTimeoutMs = 30_000
const defaultMaxRetries = 3
const backoffBaseMs = 250

/**
 * Sends requests to one Orvano server. Generated services (`orvano.health`, ...) all send
 * through a `Client`. It retries safe calls (GET, HEAD, and operations marked idempotent) on 429
 * and 503, honoring `Retry-After`, and gives every call a timeout. When someone is signed in it
 * sends their access token, refreshes it before a call when under a minute is left, and after a
 * 401 for an expired or refused token refreshes once and repeats the call once (spec 0004, AC-26).
 */
export class Client {
  /** Where the signed in user's session lives. */
  readonly session: SessionStore
  readonly #endpoint: string
  readonly #project: string | undefined
  readonly #headers: Record<string, string>
  readonly #timeoutMs: number
  readonly #maxRetries: number
  readonly #fetch: typeof fetch
  readonly #logger: Logger
  readonly #refresher: SessionRefresher
  readonly #listeners = new Set<AuthStateListener>()
  #known: AuthSession | null | undefined
  #refreshing: Promise<AuthSession | null> | undefined
  #versionChecked = false

  /** The server's base URL, without a trailing slash. */
  get endpoint(): string {
    return this.#endpoint
  }

  /** The project sent as `X-Orvano-Project`, if any. */
  get project(): string | undefined {
    return this.#project
  }

  constructor(config: ClientConfig) {
    let url: URL
    try {
      url = new URL(config.endpoint)
    } catch {
      throw new TypeError(`Orvano endpoint must be an absolute URL, got "${config.endpoint}"`)
    }
    this.#endpoint = url.href.replace(/\/+$/, '')
    this.#project = config.project
    this.#headers = { ...config.headers }
    this.session =
      config.session ??
      (hasLocalStorage() ? new LocalStorageSessionStore(config.project) : new MemorySessionStore())
    this.#refresher = config.refresh ?? refreshWithToken
    this.#timeoutMs = config.timeoutMs ?? defaultTimeoutMs
    this.#maxRetries = config.maxRetries ?? defaultMaxRetries
    // Bound, because some runtimes (Cloudflare Workers) reject a fetch called on another `this`.
    this.#fetch = config.fetch ?? globalThis.fetch.bind(globalThis)
    this.#logger = config.logger ?? console
    if (this.session.subscribe !== undefined) {
      // What this tab holds now, so a change from another tab reads as a refresh or a new sign in.
      const current = this.session.get()
      if (!(current instanceof Promise)) this.#known = current
      this.session.subscribe((session) => {
        this.#changedElsewhere(session)
      })
    }
  }

  /**
   * Calls `listener` with every change to the signed in user: `signedIn`, `signedOut`,
   * `tokenRefreshed`, and `userUpdated`, including changes another tab made. Returns a function
   * that stops it.
   */
  onAuthStateChange(listener: AuthStateListener): () => void {
    this.#listeners.add(listener)
    return () => {
      this.#listeners.delete(listener)
    }
  }

  /**
   * The signed in user's session, refreshed first when under a minute of its access token is
   * left; null when nobody is signed in. Nothing refreshes on a timer; call this when your app
   * wakes up, for example.
   */
  async getSession(): Promise<AuthSession | null> {
    return this.#sessionForCall()
  }

  /**
   * Adds this client's credentials to an outgoing request: the signed in user's access token as
   * `Authorization: Bearer`, when there is one. Clients for other audiences add theirs here too.
   */
  protected authorize(headers: Headers, session: AuthSession | null): Promise<void> {
    if (session !== null) headers.set(authorizationHeader, `Bearer ${session.accessToken}`)
    return Promise.resolve()
  }

  /**
   * Sends one request and returns the parsed JSON body. Throws {@link OrvanoError} when the
   * server answers with a failure status, and the signal's reason when it is cancelled or times out.
   */
  async request<T>(spec: RequestSpec, options?: RequestOptions): Promise<T> {
    const asUser =
      spec.anonymous !== true &&
      spec.bearer === undefined &&
      spec.session !== 'start' &&
      spec.session !== 'refresh'
    const session = asUser ? await this.#sessionForCall() : null
    try {
      return await this.#send<T>(spec, options, session)
    } catch (error) {
      // Refused before it did anything, so repeating any method once is safe.
      if (session === null || !isStaleToken(error)) throw error
      const fresh = await this.#refresh(session)
      if (fresh === null) throw error
      return this.#send<T>(spec, options, fresh)
    }
  }

  async #send<T>(
    spec: RequestSpec,
    options: RequestOptions | undefined,
    session: AuthSession | null,
  ): Promise<T> {
    const url = new URL(this.#endpoint + spec.path)
    for (const [key, value] of Object.entries(spec.query ?? {})) {
      if (value !== undefined) url.searchParams.set(key, String(value))
    }
    const body = spec.body === undefined ? undefined : JSON.stringify(spec.body)
    const signal = this.#signal(options)
    const retryable = spec.method === 'GET' || spec.method === 'HEAD' || spec.idempotent === true

    for (let attempt = 0; ; attempt++) {
      const headers = new Headers(this.#headers)
      headers.set('Accept', 'application/json')
      headers.set(sdkHeader, `${sdkName}/${sdkVersion}`)
      if (this.#project !== undefined) headers.set('X-Orvano-Project', this.#project)
      if (spec.bearer === undefined) await this.authorize(headers, session)
      else headers.set(authorizationHeader, `Bearer ${spec.bearer}`)
      if (spec.noCache === true) headers.set('Cache-Control', 'no-cache')

      const init: RequestInit = { method: spec.method, headers }
      if (body !== undefined) {
        headers.set('Content-Type', 'application/json')
        init.body = body
      }
      if (signal !== undefined) init.signal = signal

      const response = await this.#fetch(url, init)
      this.#checkVersion(response)
      if (response.ok) {
        const result: unknown =
          response.status === 204 || spec.method === 'HEAD' ? undefined : await response.json()
        await this.#applySession(spec.session, result)
        return result as T
      }

      if (
        retryable &&
        attempt < this.#maxRetries &&
        (response.status === 429 || response.status === 503)
      ) {
        await response.body?.cancel()
        await sleep(retryDelayMs(response, attempt), signal)
        continue
      }
      throw await OrvanoError.fromResponse(response)
    }
  }

  /** The stored session, refreshed first when under {@link refreshMarginMs} are left. */
  async #sessionForCall(): Promise<AuthSession | null> {
    const session = await this.session.get()
    this.#known ??= session
    if (
      session === null ||
      Date.parse(session.accessTokenExpiresAt) - Date.now() >= refreshMarginMs
    )
      return session
    try {
      return await this.#refresh(session)
    } catch {
      // A network error or timeout keeps the session; the call goes ahead with the current token.
      return session
    }
  }

  /**
   * Trades `stale` for a fresh session, once per client at a time and, when the store is shared by
   * tabs, under its Web Lock. Returns null when the session is over (a 401: the store is cleared
   * and listeners hear `signedOut`); rethrows anything else, keeping the session.
   */
  #refresh(stale: AuthSession): Promise<AuthSession | null> {
    this.#refreshing ??= (async () => {
      try {
        const locks = (globalThis as { navigator?: { locks?: LockManagerLike } }).navigator?.locks
        const lockName = this.session.lockName
        return lockName !== undefined && locks !== undefined
          ? await locks.request(lockName, () => this.#refreshNow(stale))
          : await this.#refreshNow(stale)
      } finally {
        this.#refreshing = undefined
      }
    })()
    return this.#refreshing
  }

  async #refreshNow(stale: AuthSession): Promise<AuthSession | null> {
    // Another tab, or a call racing this one, may have refreshed already.
    const current = await this.session.get()
    if (current === null) return null
    if (current.accessToken !== stale.accessToken) {
      this.#known = current
      return current
    }
    try {
      const fresh = await this.#refresher(current, this)
      await this.#save(fresh, 'tokenRefreshed')
      return fresh
    } catch (error) {
      if (!(error instanceof OrvanoError) || error.status !== 401) throw error
      await this.#save(null, 'signedOut')
      return null
    }
  }

  /** Stores or clears the session after a successful sign in, refresh, or sign out. */
  async #applySession(change: RequestSpec['session'], result: unknown): Promise<void> {
    switch (change) {
      case undefined:
        return
      case 'start':
        await this.#save(
          sessionFrom((result as { session?: unknown } | undefined)?.session),
          'signedIn',
        )
        return
      case 'refresh':
        await this.#save(sessionFrom(result), 'tokenRefreshed')
        return
      case 'end':
        await this.#save(null, 'signedOut')
        return
      case 'user':
        this.#emit('userUpdated', await this.session.get())
        return
    }
  }

  async #save(session: AuthSession | null, event: AuthEvent): Promise<void> {
    await this.session.set(session)
    this.#known = session
    this.#emit(event, session)
  }

  /** A change another tab made: tells the listeners what it amounts to here. */
  #changedElsewhere(session: AuthSession | null): void {
    const before = this.#known
    this.#known = session
    if (session === null) {
      if (before !== null) this.#emit('signedOut', null)
    } else {
      this.#emit(before?.sessionId === session.sessionId ? 'tokenRefreshed' : 'signedIn', session)
    }
  }

  #emit(event: AuthEvent, session: AuthSession | null): void {
    for (const listener of [...this.#listeners]) {
      try {
        listener(event, session)
      } catch (error) {
        this.#logger.warn(`Orvano: an onAuthStateChange listener threw: ${String(error)}`)
      }
    }
  }

  /** Warns once per client when the server's major.minor differs from this SDK's. */
  #checkVersion(response: Response): void {
    if (this.#versionChecked) return
    const version = response.headers.get(serverVersionHeader)
    if (version === null) return
    this.#versionChecked = true
    const warning = versionMismatch(version, this.#endpoint)
    if (warning !== null) this.#logger.warn(warning)
  }

  /** The call's timeout and the caller's signal, combined. */
  #signal(options: RequestOptions | undefined): AbortSignal | undefined {
    const timeoutMs = options?.timeoutMs ?? this.#timeoutMs
    const timeout = timeoutMs > 0 ? AbortSignal.timeout(timeoutMs) : undefined
    const signals = [options?.signal, timeout].filter((s) => s !== undefined)
    return signals.length > 1 ? AbortSignal.any(signals) : signals[0]
  }
}

/** The part of the Web Locks API the client uses. */
interface LockManagerLike {
  request<T>(name: string, callback: () => Promise<T>): Promise<T>
}

/** A 401 that a fresh access token may cure: `token_expired` or `invalid_token`. */
function isStaleToken(error: unknown): boolean {
  return (
    error instanceof OrvanoError &&
    error.status === 401 &&
    (error.code === 'token_expired' || error.code === 'invalid_token')
  )
}

/** `Retry-After` (seconds or an HTTP date), else exponential backoff with full jitter. */
function retryDelayMs(response: Response, attempt: number): number {
  const header = response.headers.get('Retry-After')
  if (header !== null) {
    const seconds = Number(header)
    if (Number.isFinite(seconds) && seconds >= 0) return seconds * 1000
    const date = Date.parse(header)
    if (!Number.isNaN(date)) return Math.max(0, date - Date.now())
  }
  return Math.random() * backoffBaseMs * 2 ** attempt
}

function sleep(ms: number, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted === true) {
      reject(signal.reason as Error)
      return
    }
    const onAbort = (): void => {
      clearTimeout(timer)
      reject(signal?.reason as Error)
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}
