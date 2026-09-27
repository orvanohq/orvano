/**
 * The Orvano console's own client: `console` operations only, built on the `@orvano/js` runtime.
 * It is private and never published; only the console (and the scenario runner) use it. Console
 * routes accept only a console session (spec 0004, AC-27): the `orvano_console` cookie, which the
 * browser sends itself, never an API key or an app session.
 *
 * @packageDocumentation
 */
import { Client as BaseClient, MemorySessionStore, OrvanoError } from '@orvano/js'
import type { ClientConfig, RequestOptions, RequestSpec } from '@orvano/js'

export { OrvanoError } from '@orvano/js'
export type { ClientConfig, RequestOptions } from '@orvano/js'
export * from './generated/services.js'
export type * from './generated/models.js'

/** The console session cookie (the access token). Named only here in the console client. */
export const consoleCookie = 'orvano_console'

/** The path the console session operations live under; a refresh or sign out there is never retried. */
const sessionPath = '/v1/console/account/session'

/** Settings for the console {@link Client}. */
export type ConsoleClientConfig = Omit<ClientConfig, 'session' | 'refresh'>

/**
 * Sends console requests. In a browser the browser keeps and sends the console cookies itself (same
 * origin). Elsewhere (the scenario runner) the client keeps them in memory, honoring each cookie's
 * path, and sends `Origin` as the endpoint's origin, as the console's CSRF rule expects (AC-28).
 * After a 401 `token_expired` it calls `consoleAccount.refreshSession` once and repeats the call once.
 */
export class Client extends BaseClient {
  #refreshing: Promise<void> | undefined

  constructor(config: ConsoleClientConfig) {
    const inBrowser = typeof (globalThis as { document?: unknown }).document !== 'undefined'
    const fetch = config.fetch ?? globalThis.fetch.bind(globalThis)
    super({
      ...config,
      // Console routes never take an app session, so none is ever stored or sent here.
      session: new MemorySessionStore(),
      fetch: inBrowser ? fetch : withCookieJar(fetch, new URL(config.endpoint).origin),
    })
  }

  override async request<T>(spec: RequestSpec, options?: RequestOptions): Promise<T> {
    try {
      return await super.request<T>(spec, options)
    } catch (error) {
      const expired =
        error instanceof OrvanoError && error.status === 401 && error.code === 'token_expired'
      if (!expired || spec.path.startsWith(sessionPath)) throw error
      await this.#refresh()
      return super.request<T>(spec, options)
    }
  }

  /** One refresh at a time; a refused one throws, and the console sends you to sign in. */
  #refresh(): Promise<void> {
    this.#refreshing ??= super
      .request<undefined>({ method: 'POST', path: `${sessionPath}/refresh`, idempotent: true })
      .then(() => undefined)
      .finally(() => {
        this.#refreshing = undefined
      })
    return this.#refreshing
  }
}

/** A cookie the jar holds: its value and the path it was set for. */
interface JarCookie {
  value: string
  path: string
}

/**
 * Outside a browser: a `fetch` that keeps the cookies responses set and sends them back on later
 * requests whose path they cover, like a browser on one origin, and sends `Origin`.
 */
function withCookieJar(fetch: typeof globalThis.fetch, origin: string): typeof globalThis.fetch {
  const jar = new Map<string, JarCookie>()
  return async (input, init) => {
    const url = new URL(input instanceof Request ? input.url : input.toString())
    const headers = new Headers(init?.headers)
    const cookies = [...jar]
      .filter(
        ([, cookie]) =>
          url.pathname === cookie.path || url.pathname.startsWith(cookie.path.replace(/\/?$/, '/')),
      )
      .map(([name, cookie]) => `${name}=${cookie.value}`)
    if (cookies.length > 0) headers.set('Cookie', cookies.join('; '))
    headers.set('Origin', origin)
    const response = await fetch(input, { ...init, headers })
    for (const line of response.headers.getSetCookie()) {
      const [pair = '', ...attributes] = line.split(';').map((part) => part.trim())
      const [name = '', ...value] = pair.split('=')
      const path = attributes.find((a) => a.toLowerCase().startsWith('path='))?.slice(5) ?? '/'
      const expired = attributes.some(
        (a) => a.toLowerCase().startsWith('expires=') && Date.parse(a.slice(8)) <= Date.now(),
      )
      if (expired || value.join('=') === '') jar.delete(name)
      else jar.set(name, { value: value.join('='), path })
    }
    return response
  }
}
