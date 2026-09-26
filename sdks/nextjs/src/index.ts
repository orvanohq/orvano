/**
 * Orvano for Next.js. It wraps `@orvano/js` and has no endpoint code of its own: every operation
 * comes from the core SDK, so it can never drift from the contract. On the server the user's
 * session lives in the `orvano_session` cookie (`HttpOnly`, `Secure`, `SameSite=Lax`).
 *
 * @example
 * ```ts
 * // app/page.tsx (a server component)
 * import { cookies } from 'next/headers'
 * import { createServerClient } from '@orvano/nextjs'
 *
 * export default async function Page() {
 *   const orvano = createServerClient({
 *     endpoint: process.env.ORVANO_ENDPOINT!,
 *     cookies: await cookies(),
 *   })
 *   const { version } = await orvano.health.get()
 *   return <p>Orvano {version}</p>
 * }
 * ```
 *
 * @packageDocumentation
 */
import { Client, Orvano } from '@orvano/js'
import type { ClientConfig, SessionStore } from '@orvano/js'

export { Client, ErrorCode, Orvano, OrvanoError } from '@orvano/js'
export type { ClientConfig, RequestOptions, SessionStore } from '@orvano/js'

/**
 * The cookie the session lives in. Temporary: the auth spec (scope row 8) may replace it, and this
 * is the only place `@orvano/nextjs` names it.
 */
export const sessionCookie = 'orvano_session'

/** Options for the session cookie. */
export interface CookieOptions {
  httpOnly: boolean
  secure: boolean
  sameSite: 'lax'
  path: string
}

/**
 * The part of a Next.js cookie store the session needs: `await cookies()` in server components,
 * route handlers, and server actions, or `request.cookies` and `response.cookies` in middleware.
 */
export interface CookieStore {
  /** Reads a cookie. */
  get(name: string): { value: string } | undefined
  /** Writes a cookie. Server components can't; the session is then only read. */
  set?(name: string, value: string, options: CookieOptions): unknown
  /** Deletes a cookie. */
  delete?(name: string): unknown
}

const cookieOptions: CookieOptions = { httpOnly: true, secure: true, sameSite: 'lax', path: '/' }

/**
 * A {@link SessionStore} in the `orvano_session` cookie. Reads from `read`; writes to every store
 * in `write` (in middleware, the request and the response).
 */
export class CookieSessionStore implements SessionStore {
  readonly #read: CookieStore
  readonly #write: readonly CookieStore[]

  constructor(read: CookieStore, write: readonly CookieStore[] = [read]) {
    this.#read = read
    this.#write = write
  }

  get(): string | null {
    const value = this.#read.get(sessionCookie)?.value
    return value === undefined || value === '' ? null : value
  }

  set(token: string | null): void {
    for (const store of this.#write) {
      try {
        if (token === null) store.delete?.(sessionCookie)
        else store.set?.(sessionCookie, token, cookieOptions)
      } catch {
        // Server components can't set cookies; the session is read only there by design.
      }
    }
  }
}

/** Settings for {@link createServerClient}. */
export interface ServerClientConfig extends Omit<ClientConfig, 'session'> {
  /** The request's cookies: `await cookies()` from `next/headers`. */
  cookies: CookieStore
}

/**
 * An Orvano client for code that runs on the server for one request: server components, route
 * handlers, and server actions. The session comes from, and is saved to, the request's cookies.
 * Create one per request; never share it across requests.
 */
export function createServerClient(config: ServerClientConfig): Orvano {
  const { cookies, ...rest } = config
  return new Orvano(new Client({ ...rest, session: new CookieSessionStore(cookies) }))
}

/** Settings for {@link createMiddlewareClient}. */
export interface MiddlewareClientConfig extends Omit<ClientConfig, 'session'> {
  /** The incoming request (`NextRequest`); the session is read from its cookies. */
  request: { cookies: CookieStore }
  /** The outgoing response (`NextResponse`); a new session is written to it too. */
  response: { cookies: CookieStore }
}

/**
 * An Orvano client for middleware. The session is read from the request's cookies, and a changed
 * session is written to both the request (for the rest of this request) and the response.
 */
export function createMiddlewareClient(config: MiddlewareClientConfig): Orvano {
  const { request, response, ...rest } = config
  const session = new CookieSessionStore(request.cookies, [request.cookies, response.cookies])
  return new Orvano(new Client({ ...rest, session }))
}

const browserClients = new Map<string, Orvano>()

/**
 * An Orvano client for client components. Returns one shared instance per endpoint and project,
 * so calling it on every render is cheap. The `orvano_session` cookie is `HttpOnly`, so browser
 * code never sees the token; calls that need the session belong in server code.
 */
export function createBrowserClient(config: ClientConfig): Orvano {
  const key = `${config.endpoint}\n${config.project ?? ''}`
  let orvano = browserClients.get(key)
  if (orvano === undefined) {
    orvano = new Orvano(new Client(config))
    browserClients.set(key, orvano)
  }
  return orvano
}
