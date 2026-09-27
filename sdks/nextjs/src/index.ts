/**
 * Orvano for Next.js. It wraps `@orvano/js` and has no endpoint code of its own: every operation
 * comes from the core SDK, so it can never drift from the contract. The user's session lives in two
 * cookies (spec 0004, AC-23): `orvano_access` holds the access token and `orvano_refresh`
 * (`HttpOnly`) the refresh token, both `Secure`, `SameSite=Lax`, and host only. The middleware
 * helper `updateSession` and the route handler `createOrvanoRouteHandler` live in
 * `@orvano/nextjs/server`.
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
import { Client, MemorySessionStore, Orvano, OrvanoError } from '@orvano/js'
import type { AuthSession, ClientConfig, SessionRefresher, SessionStore } from '@orvano/js'

export { Client, ErrorCode, Orvano, OrvanoError } from '@orvano/js'
export type {
  AuthEvent,
  AuthSession,
  AuthStateListener,
  ClientConfig,
  RequestOptions,
  SessionStore,
} from '@orvano/js'

/** The cookie that holds the access token; browser code can read it. Named only here. */
export const accessCookie = 'orvano_access'

/** The `HttpOnly` cookie that holds the refresh token. Named only here. */
export const refreshCookie = 'orvano_refresh'

/** Where the app mounts {@link createOrvanoRouteHandler} unless told otherwise. */
export const defaultHandlerPath = '/api/orvano'

/** The header the browser's IP travels in from the Next.js server to Orvano (spec 0004, AC-31). */
export const clientIpHeader = 'X-Orvano-Client-IP'

/** The header the browser's user agent travels in from the Next.js server to Orvano. */
export const clientUserAgentHeader = 'X-Orvano-Client-UA'

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

/** Request headers, as `await headers()` or `request.headers` give them. */
export interface HeadersLike {
  get(name: string): string | null
}

/**
 * Whether the session cookies get `Secure`: always, except when the app's own URL is plain
 * `http://localhost`, where some browsers drop secure cookies.
 */
export function secureCookies(appUrl: string | URL): boolean {
  const url = new URL(appUrl)
  return !(url.protocol === 'http:' && url.hostname === 'localhost')
}

/**
 * The browser's IP (the first `x-forwarded-for` value, else `x-real-ip`) and user agent, as the
 * headers Orvano records on the session instead of the Next.js server's own. Shown only; Orvano
 * never uses them for limits or checks.
 */
export function forwardedClientHeaders(headers: HeadersLike): Record<string, string> {
  const forwarded = headers.get('x-forwarded-for')?.split(',')[0]?.trim()
  const ip =
    forwarded === undefined || forwarded === '' ? headers.get('x-real-ip')?.trim() : forwarded
  const userAgent = headers.get('user-agent')
  return {
    ...(ip === undefined || ip === '' ? {} : { [clientIpHeader]: ip }),
    ...(userAgent === null || userAgent === '' ? {} : { [clientUserAgentHeader]: userAgent }),
  }
}

/** The `exp` and `sid` claims of an access token, or null when it is not a readable JWT. */
export function accessClaims(token: string): { exp: number; sid: string } | null {
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

/** The options each session cookie is written with. */
export function cookieOptions(httpOnly: boolean, secure: boolean, expires: Date): CookieOptions {
  return { httpOnly, secure, sameSite: 'lax', path: '/', expires }
}

/**
 * A {@link SessionStore} in the `orvano_access` and `orvano_refresh` cookies. Reads from `read`;
 * writes to every store in `write` (in middleware, the request and the response).
 */
export class CookieSessionStore implements SessionStore {
  readonly #read: CookieStore
  readonly #write: readonly CookieStore[]
  readonly #secure: boolean

  /** `secure` is false only for an app on plain `http://localhost` (see {@link secureCookies}). */
  constructor(
    read: CookieStore,
    write: readonly CookieStore[] = [read],
    options: { secure?: boolean } = {},
  ) {
    this.#read = read
    this.#write = write
    this.#secure = options.secure ?? true
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
        store.set?.(
          accessCookie,
          session.accessToken,
          cookieOptions(false, this.#secure, new Date(session.accessTokenExpiresAt)),
        )
        if (session.refreshToken !== null && session.refreshTokenExpiresAt !== null) {
          store.set?.(
            refreshCookie,
            session.refreshToken,
            cookieOptions(true, this.#secure, new Date(session.refreshTokenExpiresAt)),
          )
        }
      } catch {
        // Server components can't set cookies; the session is read only there by design.
      }
    }
  }
}

/** Settings for {@link createServerClient}. */
export interface ServerClientConfig extends Omit<ClientConfig, 'session' | 'refresh'> {
  /** The request's cookies: `await cookies()` from `next/headers`. */
  cookies: CookieStore
  /**
   * The request's headers (`await headers()`), so Orvano records the browser's IP and user agent
   * on a new session instead of this server's.
   */
  requestHeaders?: HeadersLike
  /** Your app's own URL; cookies skip `Secure` only when it is `http://localhost`. */
  appUrl?: string | URL
}

/**
 * An Orvano client for code that runs on the server for one request: server components, route
 * handlers, and server actions. The session comes from, and is saved to, the request's cookies;
 * route handlers and server actions may refresh it, server components only read it. Create one
 * per request; never share it across requests.
 */
export function createServerClient(config: ServerClientConfig): Orvano {
  const { cookies, requestHeaders, appUrl, ...rest } = config
  const secure = appUrl === undefined ? true : secureCookies(appUrl)
  return new Orvano(
    new Client({
      ...rest,
      headers: {
        ...rest.headers,
        ...(requestHeaders === undefined ? {} : forwardedClientHeaders(requestHeaders)),
      },
      session: new CookieSessionStore(cookies, [cookies], { secure }),
    }),
  )
}

/** Settings for {@link createMiddlewareClient}. */
export interface MiddlewareClientConfig extends Omit<ClientConfig, 'session' | 'refresh'> {
  /** The incoming request (`NextRequest`); the session is read from its cookies. */
  request: { cookies: CookieStore; headers?: HeadersLike; url?: string }
  /** The outgoing response (`NextResponse`); a new session is written to it too. */
  response: { cookies: CookieStore }
}

/**
 * An Orvano client for middleware. The session is read from the request's cookies, and a changed
 * session is written to both the request (for the rest of this request) and the response. Most
 * apps only need `updateSession` from `@orvano/nextjs/server`.
 */
export function createMiddlewareClient(config: MiddlewareClientConfig): Orvano {
  const { request, response, ...rest } = config
  const secure = request.url === undefined ? true : secureCookies(request.url)
  const session = new CookieSessionStore(request.cookies, [request.cookies, response.cookies], {
    secure,
  })
  return new Orvano(
    new Client({
      ...rest,
      headers: {
        ...rest.headers,
        ...(request.headers === undefined ? {} : forwardedClientHeaders(request.headers)),
      },
      session,
    }),
  )
}

/**
 * The browser's view of the session: it reads `orvano_access` (it can never read the `HttpOnly`
 * `orvano_refresh`). Signing out clears the access cookie and asks the app's route handler to end
 * the session and clear both cookies. Sign users in on the server (a server action or route
 * handler with {@link createServerClient}); a session this store receives in the browser keeps
 * only its access token, so it lasts at most 15 minutes.
 */
export class BrowserCookieSessionStore implements SessionStore {
  readonly #handlerPath: string
  readonly lockName: string

  constructor(project: string | undefined, handlerPath = defaultHandlerPath) {
    this.#handlerPath = handlerPath
    this.lockName = `orvano.refresh.${project ?? ''}`
  }

  get(): AuthSession | null {
    const accessToken = readDocumentCookie(accessCookie)
    if (accessToken === null) return null
    const claims = accessClaims(accessToken)
    return claims === null
      ? null
      : {
          accessToken,
          accessTokenExpiresAt: new Date(claims.exp * 1000).toISOString(),
          refreshToken: null,
          refreshTokenExpiresAt: null,
          sessionId: claims.sid,
        }
  }

  set(session: AuthSession | null): void {
    const doc = (globalThis as { document?: { cookie: string } }).document
    if (doc === undefined) return
    const location = (globalThis as { location?: { protocol: string } }).location
    const secure = location?.protocol === 'https:' ? '; Secure' : ''
    if (session === null) {
      doc.cookie = `${accessCookie}=; Path=/; Max-Age=0; SameSite=Lax${secure}`
      void globalThis
        .fetch(`${this.#handlerPath}/signout`, { method: 'POST', credentials: 'same-origin' })
        .catch(() => undefined)
      return
    }
    const expires = new Date(session.accessTokenExpiresAt).toUTCString()
    doc.cookie = `${accessCookie}=${session.accessToken}; Path=/; Expires=${expires}; SameSite=Lax${secure}`
  }
}

function readDocumentCookie(name: string): string | null {
  const doc = (globalThis as { document?: { cookie: string } }).document
  if (doc === undefined) return null
  for (const part of doc.cookie.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === name) {
      const text = value.join('=')
      return text === '' ? null : text
    }
  }
  return null
}

/**
 * The browser's {@link SessionRefresher}: asks the app's route handler, which reads the `HttpOnly`
 * refresh cookie, trades it, and sets both cookies again.
 */
export function refreshThroughHandler(handlerPath = defaultHandlerPath): SessionRefresher {
  return async () => {
    const response = await globalThis.fetch(`${handlerPath}/refresh`, {
      method: 'POST',
      credentials: 'same-origin',
    })
    if (!response.ok) throw await OrvanoError.fromResponse(response)
    const body = (await response.json()) as {
      accessToken: string
      accessTokenExpiresAt: string
      sessionId: string
    }
    return {
      accessToken: body.accessToken,
      accessTokenExpiresAt: body.accessTokenExpiresAt,
      refreshToken: null,
      refreshTokenExpiresAt: null,
      sessionId: body.sessionId,
    }
  }
}

/** Settings for {@link createBrowserClient}. */
export interface BrowserClientConfig extends Omit<ClientConfig, 'session' | 'refresh'> {
  /** Where the app mounts `createOrvanoRouteHandler`. Defaults to `/api/orvano`. */
  handlerPath?: string
}

const browserClients = new Map<string, Orvano>()

/**
 * An Orvano client for client components. Returns one shared instance per endpoint and project,
 * so calling it on every render is cheap. It sends the `orvano_access` cookie's token and
 * refreshes through the app's route handler (`createOrvanoRouteHandler`, mounted at
 * `app/api/orvano/[...orvano]/route.ts`) when under a minute of it is left.
 */
export function createBrowserClient(config: BrowserClientConfig): Orvano {
  const { handlerPath, ...rest } = config
  const key = `${config.endpoint}\n${config.project ?? ''}\n${handlerPath ?? ''}`
  let orvano = browserClients.get(key)
  if (orvano === undefined) {
    const inBrowser = typeof (globalThis as { document?: unknown }).document !== 'undefined'
    orvano = new Orvano(
      new Client({
        ...rest,
        session: inBrowser
          ? new BrowserCookieSessionStore(config.project, handlerPath)
          : new MemorySessionStore(),
        refresh: refreshThroughHandler(handlerPath),
      }),
    )
    browserClients.set(key, orvano)
  }
  return orvano
}
