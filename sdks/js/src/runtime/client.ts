import { OrvanoError } from './error.js'

/** Settings for a {@link Client}. */
export interface ClientConfig {
  /** Your Orvano server's base URL, for example `https://orvano.example.com`. */
  endpoint: string
  /** The project ID, sent as `X-Orvano-Project` on every call. */
  project?: string
  /** Extra headers sent with every request. */
  headers?: Record<string, string>
  /** A custom `fetch`, for tests or runtimes without a global one. */
  fetch?: typeof fetch
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
  query?: Record<string, string | number | boolean | undefined>
  /** The JSON body. */
  body?: unknown
  /** Marked `x-orvano-idempotent` in the contract, so it is safe to retry. */
  idempotent?: boolean
}

/**
 * Sends requests to one Orvano server. Generated services (`orvano.health`, ...) all send
 * through a `Client`.
 */
export class Client {
  readonly #endpoint: string
  readonly #project: string | undefined
  readonly #headers: Record<string, string>
  readonly #fetch: typeof fetch

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
    // Bound, because some runtimes (Cloudflare Workers) reject a fetch called on another `this`.
    this.#fetch = config.fetch ?? globalThis.fetch.bind(globalThis)
  }

  /**
   * Sends one request and returns the parsed JSON body. Throws {@link OrvanoError} when the
   * server answers with a failure status.
   */
  async request<T>(spec: RequestSpec): Promise<T> {
    const url = new URL(this.#endpoint + spec.path)
    for (const [key, value] of Object.entries(spec.query ?? {})) {
      if (value !== undefined) url.searchParams.set(key, String(value))
    }

    const headers = new Headers(this.#headers)
    headers.set('Accept', 'application/json')
    if (this.#project !== undefined) headers.set('X-Orvano-Project', this.#project)

    const init: RequestInit = { method: spec.method, headers }
    if (spec.body !== undefined) {
      headers.set('Content-Type', 'application/json')
      init.body = JSON.stringify(spec.body)
    }

    const response = await this.#fetch(url, init)
    if (!response.ok) throw await OrvanoError.fromResponse(response)
    if (response.status === 204 || spec.method === 'HEAD') return undefined as T
    return (await response.json()) as T
  }
}
