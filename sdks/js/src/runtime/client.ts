import { MemorySessionStore, sessionHeader } from './auth.js'
import type { SessionStore } from './auth.js'
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
  /** Where the signed in user's session lives. Defaults to memory. */
  session?: SessionStore
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
}

const defaultTimeoutMs = 30_000
const defaultMaxRetries = 3
const backoffBaseMs = 250

/**
 * Sends requests to one Orvano server. Generated services (`orvano.health`, ...) all send
 * through a `Client`. It retries safe calls (GET, HEAD, and operations marked idempotent) on 429
 * and 503, honoring `Retry-After`, and gives every call a timeout.
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
  #versionChecked = false

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
    this.session = config.session ?? new MemorySessionStore()
    this.#timeoutMs = config.timeoutMs ?? defaultTimeoutMs
    this.#maxRetries = config.maxRetries ?? defaultMaxRetries
    // Bound, because some runtimes (Cloudflare Workers) reject a fetch called on another `this`.
    this.#fetch = config.fetch ?? globalThis.fetch.bind(globalThis)
    this.#logger = config.logger ?? console
  }

  /**
   * Adds this client's credentials to an outgoing request: the session token, when there is one.
   * Clients for other audiences add theirs here too.
   */
  protected async authorize(headers: Headers): Promise<void> {
    const token = await this.session.get()
    if (token !== null && token !== '') headers.set(sessionHeader, token)
  }

  /**
   * Sends one request and returns the parsed JSON body. Throws {@link OrvanoError} when the
   * server answers with a failure status, and the signal's reason when it is cancelled or times out.
   */
  async request<T>(spec: RequestSpec, options?: RequestOptions): Promise<T> {
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
      await this.authorize(headers)

      const init: RequestInit = { method: spec.method, headers }
      if (body !== undefined) {
        headers.set('Content-Type', 'application/json')
        init.body = body
      }
      if (signal !== undefined) init.signal = signal

      const response = await this.#fetch(url, init)
      this.#checkVersion(response)
      if (response.ok) {
        if (response.status === 204 || spec.method === 'HEAD') return undefined as T
        return (await response.json()) as T
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
