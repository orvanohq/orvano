/**
 * The Orvano console's own client: `console` operations only, built on the `@orvano/js` runtime.
 * It is private and never published; only the console (and the scenario runner) use it. Console
 * routes accept only a console session, never an API key or an app session.
 *
 * @packageDocumentation
 */
import { Client as BaseClient, MemorySessionStore } from '@orvano/js'
import type { ClientConfig } from '@orvano/js'

export { OrvanoError } from '@orvano/js'
export type { ClientConfig, RequestOptions } from '@orvano/js'
export * from './generated/services.js'
export type * from './generated/models.js'

/**
 * The cookie a console session travels in. Temporary: scope rows 7 and 8 set the real format and
 * its CSRF rule, and this is the only place the console client names it.
 */
export const consoleCookie = 'orvano_console'

/** Settings for the console {@link Client}. */
export interface ConsoleClientConfig extends Omit<ClientConfig, 'session'> {
  /** Outside a browser (tests), the console session token to send as the cookie. */
  consoleToken?: string | undefined
}

/**
 * Sends console requests. In a browser the browser sends the console cookie itself (same origin);
 * elsewhere (tests) `consoleToken` is sent as that cookie.
 */
export class Client extends BaseClient {
  readonly #consoleToken: string | undefined

  constructor(config: ConsoleClientConfig) {
    const { consoleToken, ...rest } = config
    // Console routes never take an app session, so none is ever stored or refreshed here.
    super({ ...rest, session: new MemorySessionStore() })
    this.#consoleToken = consoleToken
  }

  protected override authorize(headers: Headers): Promise<void> {
    // Never the base client's bearer token: console routes answer 401 to an app session.
    if (typeof (globalThis as { document?: unknown }).document !== 'undefined')
      return Promise.resolve()
    if (this.#consoleToken !== undefined && this.#consoleToken !== '')
      headers.set('Cookie', `${consoleCookie}=${this.#consoleToken}`)
    return Promise.resolve()
  }
}
