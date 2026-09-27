/**
 * Orvano for Next.js. It wraps `@orvano/js` and has no endpoint code of its own: every operation
 * comes from the core SDK, so it can never drift from the contract. The user's session lives in two
 * cookies (spec 0004, AC-23): `orvano_access` holds the access token and `orvano_refresh`
 * (`HttpOnly`) the refresh token, both `Secure`, `SameSite=Lax`, and host only.
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
import type { ClientConfig, AuthSession, SessionStore } from '@orvano/js'

export { Client, ErrorCode, Orvano, OrvanoError } from '@orvano/js'
export type { ClientConfig, RequestOptions, AuthSession, SessionStore } from '@orvano/js'

/** The cookie that holds the access token; browser code can read it. Named only here. */
export const accessCookie = 'orvano_access'

/** The `HttpOnly` cookie that holds the refresh token. Named only here. */
export const refreshCookie = 'orvano_refresh'

/** Options for a session cookie. Neither has a `Domain`, so sibling subdomains never see them. */
export interface CookieOptions {
  httpOnly: boolean
  secure: boolean
  sameSite: 'lax'
  path: string
  expires: Date
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

/** The `exp` and `sid` claims of an access token, or null when it is not a readable JWT. */
function accessClaims(token: string): { exp: number; sid: string } | null {
  const payload = token.split('.')[1]
  if (payload === undefined) return null
  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const claims = JSON.parse(json) as { exp?: unknown; sid?: unknown }
    return typeof claims.exp === 'number' && typeof claims.sid === 'string'
      ? { exp: claims.exp, sid: claims.sid }
      : null
  } catch {
    return null
  }
}

/**
 * A {@link SessionStore} in the `orvano_access` and `orvano_refresh` cookies. Reads from `read`;
 * writes to every store in `write` (in middleware, the request and the response).
 */
export class CookieSessionStore implements SessionStore {
  readonly #read: CookieStore
  readonly #write: readonly CookieStore[]

  constructor(read: CookieStore, write: readonly CookieStore[] = [read]) {
    this.#read = read
    this.#write = write
  }

  get(): AuthSession | null {
    const accessToken = this.#read.get(accessCookie)?.value
    if (accessToken === undefined || accessToken === '') return null
    const claims = accessClaims(accessToken)
    if (claims === null) return null
    const refreshToken = this.#read.get(refreshCookie)?.value
    return {
      accessToken,
      accessTokenExpiresAt: new Date(claims.exp * 1000).toISOString(),
      refreshToken: refreshToken === undefined || refreshToken === '' ? null : refreshToken,
      refreshTokenExpiresAt: null,
      sessionId: claims.sid,
    }
  }

  set(session: AuthSession | null): void {
    for (const store of this.#write) {
      try {
        if (session === null) {
          store.delete?.(accessCookie)
          store.delete?.(refreshCookie)
          continue
        }
        store.set?.(accessCookie, session.accessToken, {
          httpOnly: false,
          secure: true,
          sameSite: 'lax',
          path: '/',
          expires: new Date(session.accessTokenExpiresAt),
        })
        if (session.refreshToken !== null && session.refreshTokenExpiresAt !== null) {
          store.set?.(refreshCookie, session.refreshToken, {
            httpOnly: true,
            secure: true,
            sameSite: 'lax',
            path: '/',
            expires: new Date(session.refreshTokenExpiresAt),
          })
        }
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
 * so calling it on every render is cheap. AuthSession handling in the browser (reading `orvano_access`,
 * refreshing through the app's route handler) arrives with spec 0004's client session work.
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
