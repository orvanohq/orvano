/**
 * The Orvano console's own client: `console` operations only, built on the `@orvano/js` runtime.
 * It is private and never published; only the console (and the scenario runner) use it. Console
 * routes accept only a console session, never an API key or an app session.
 *
 * @packageDocumentation
 */
import { Client as BaseClient } from '@orvano/js'

export { MemorySessionStore, OrvanoError } from '@orvano/js'
export type { ClientConfig, RequestOptions, SessionStore } from '@orvano/js'
export * from './generated/services.js'
export type * from './generated/models.js'

/**
 * The cookie a console session travels in. Temporary: scope rows 7 and 8 set the real format and
 * its CSRF rule, and this is the only place the console client names it.
 */
export const consoleCookie = 'orvano_console'

/**
 * Sends console requests. In a browser the browser sends the console cookie itself (same origin);
 * elsewhere (tests) the token in `session` is sent as that cookie.
 */
export class Client extends BaseClient {
  protected override async authorize(headers: Headers): Promise<void> {
    // Never the base client's app session header: console routes answer 401 to it.
    if (typeof (globalThis as { document?: unknown }).document !== 'undefined') return
    const token = await this.session.get()
    if (token !== null && token !== '') headers.set('Cookie', `${consoleCookie}=${token}`)
  }
}
