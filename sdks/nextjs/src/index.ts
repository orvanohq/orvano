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
import type {
  AuthSession,
  ClientConfig,
  EmailAuthTransport,
  EmailCodeResult,
  LinkResult,
  MfaFactor,
  MfaTransport,
  OAuthTransport,
  PasskeyChallenge,
  PendingMfaStore,
  PendingMfaTicket,
  RequestOptions,
  SessionRefresher,
  SessionStore,
  SignInOutcome,
} from '@orvano/js'

import { decodeMfaCookie, encodeMfaCookie } from './cookie-codec.js'

export { Client, ErrorCode, Orvano, OrvanoError } from '@orvano/js'
export type {
  AuthEvent,
  AuthSession,
  AuthStateListener,
  ClientConfig,
  EmailCodeResult,
  EmailLinkType,
  LinkResult,
  MfaAnswer,
  MfaWireAnswer,
  PasskeyAuthenticator,
  PasskeyRegistrationOptions,
  PasskeySignInOptions,
  PendingMfa,
  RequestOptions,
  SessionStore,
  SignInOutcome,
} from '@orvano/js'

/** The cookie that holds the access token; browser code can read it. Named only here. */
export const accessCookie = 'orvano_access'

/** The `HttpOnly` cookie that holds the refresh token. Named only here. */
export const refreshCookie = 'orvano_refresh'

/**
 * The `HttpOnly` cookie that holds the MFA ticket and `next` while a sign in waits at the MFA step
 * (spec 0013, AC-37). Named only here.
 */
export const mfaCookie = 'orvano_mfa'

/** How long the `orvano_mfa` cookie lives: the ticket's 5 minutes. */
export const mfaCookieSeconds = 300

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

/** Options for the `orvano_mfa` cookie: `HttpOnly`, host only, as long as the ticket lives. */
export interface MfaCookieOptions {
  httpOnly: true
  secure: boolean
  sameSite: 'lax'
  path: string
  maxAge: number
}

/** The options the `orvano_mfa` cookie is written with. */
export function mfaCookieOptions(secure: boolean): MfaCookieOptions {
  return { httpOnly: true, secure, sameSite: 'lax', path: '/', maxAge: mfaCookieSeconds }
}

/**
 * The part of a Next.js cookie store the session needs: `await cookies()` in server components,
 * route handlers, and server actions, or `request.cookies` and `response.cookies` in middleware.
 */
export interface CookieStore {
  /** Reads a cookie. */
  get(name: string): { value: string } | undefined
  /** Writes a cookie. Server components can't; the session is then only read. */
  set?(name: string, value: string, options: CookieOptions | MfaCookieOptions): unknown
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
 * Finds the visitor's IP address in the request that reached your Next.js server (spec 0014,
 * AC-36), or `null` when there is none. Orvano records it on new sessions, and, once your server's
 * address is listed on the project's App servers card, counts its rate limits by it.
 */
export type ClientIpResolver = (request: { headers: HeadersLike }) => string | null | undefined

/**
 * The default {@link ClientIpResolver}: `x-real-ip`, else the last (rightmost) `x-forwarded-for`
 * value, the one the proxy nearest your server appended. Never the first value, which the visitor
 * can set to anything. Behind several proxies or a CDN, pass your own `clientIp` that reads the
 * header your host guarantees (the docs list one per host).
 */
export function defaultClientIp(request: { headers: HeadersLike }): string | null {
  const realIp = request.headers.get('x-real-ip')?.trim()
  if (realIp !== undefined && realIp !== '') return realIp
  const last = request.headers.get('x-forwarded-for')?.split(',').at(-1)?.trim()
  return last === undefined || last === '' ? null : last
}

/**
 * The visitor's IP (from `clientIp`, by default {@link defaultClientIp}) and user agent, as the
 * headers Orvano reads instead of this server's own: `X-Orvano-Client-IP` and
 * `X-Orvano-Client-UA`.
 */
export function forwardedClientHeaders(
  headers: HeadersLike,
  clientIp: ClientIpResolver = defaultClientIp,
): Record<string, string> {
  const ip = clientIp({ headers })?.trim()
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

/**
 * A {@link PendingMfaStore} in the `HttpOnly` `orvano_mfa` cookie (spec 0013, AC-37): a server
 * side sign in that stops at the MFA step keeps its ticket there, never in what the call returns,
 * and the route handler's `mfa` action finishes it. Reads from `read`; writes to every store in
 * `write`.
 */
export class CookiePendingMfaStore implements PendingMfaStore {
  /** The ticket is in an `HttpOnly` cookie, so the client blanks it in the sign in's result. */
  readonly hidesTicket = true
  readonly #read: CookieStore
  readonly #write: readonly CookieStore[]
  readonly #secure: boolean
  readonly #next: string

  /** `next` is the app path the `mfa` action answers once the sign in finishes; default `/`. */
  constructor(
    read: CookieStore,
    write: readonly CookieStore[] = [read],
    options: { secure?: boolean; next?: string } = {},
  ) {
    this.#read = read
    this.#write = write
    this.#secure = options.secure ?? true
    this.#next = options.next ?? '/'
  }

  get(): PendingMfaTicket | null {
    const cookie = decodeMfaCookie(this.#read.get(mfaCookie)?.value)
    return cookie === null
      ? null
      : {
          ticket: cookie.ticket,
          factors: cookie.factors as MfaFactor[],
          expiresAt: cookie.expiresAt,
        }
  }

  set(pending: PendingMfaTicket | null): void {
    for (const store of this.#write) {
      try {
        if (pending === null) store.delete?.(mfaCookie)
        else
          store.set?.(
            mfaCookie,
            encodeMfaCookie({ ...pending, next: this.#next }),
            mfaCookieOptions(this.#secure),
          )
      } catch {
        // Server components can't set cookies; sign in from a server action or route handler.
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
  /** Finds the visitor's IP in `requestHeaders`; defaults to {@link defaultClientIp}. */
  clientIp?: ClientIpResolver
}

/**
 * An Orvano client for code that runs on the server for one request: server components, route
 * handlers, and server actions. The session comes from, and is saved to, the request's cookies;
 * route handlers and server actions may refresh it, server components only read it. Create one
 * per request; never share it across requests.
 */
export function createServerClient(config: ServerClientConfig): Orvano {
  const { cookies, requestHeaders, appUrl, clientIp, ...rest } = config
  const secure = appUrl === undefined ? true : secureCookies(appUrl)
  return new Orvano(
    new Client({
      ...rest,
      headers: {
        ...rest.headers,
        ...(requestHeaders === undefined ? {} : forwardedClientHeaders(requestHeaders, clientIp)),
      },
      session: new CookieSessionStore(cookies, [cookies], { secure }),
      mfaStore: new CookiePendingMfaStore(cookies, [cookies], { secure }),
    }),
  )
}

/** Settings for {@link createMiddlewareClient}. */
export interface MiddlewareClientConfig extends Omit<ClientConfig, 'session' | 'refresh'> {
  /** The incoming request (`NextRequest`); the session is read from its cookies. */
  request: { cookies: CookieStore; headers?: HeadersLike; url?: string }
  /** The outgoing response (`NextResponse`); a new session is written to it too. */
  response: { cookies: CookieStore }
  /** Finds the visitor's IP in the request; defaults to {@link defaultClientIp}. */
  clientIp?: ClientIpResolver
}

/**
 * An Orvano client for middleware. The session is read from the request's cookies, and a changed
 * session is written to both the request (for the rest of this request) and the response. Most
 * apps only need `updateSession` from `@orvano/nextjs/server`.
 */
export function createMiddlewareClient(config: MiddlewareClientConfig): Orvano {
  const { request, response, clientIp, ...rest } = config
  const secure = request.url === undefined ? true : secureCookies(request.url)
  const session = new CookieSessionStore(request.cookies, [request.cookies, response.cookies], {
    secure,
  })
  return new Orvano(
    new Client({
      ...rest,
      headers: {
        ...rest.headers,
        ...(request.headers === undefined ? {} : forwardedClientHeaders(request.headers, clientIp)),
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

/**
 * The browser's {@link EmailAuthTransport} (spec 0010, AC-25): links and codes go to the app's
 * route handler (`.../redeem` and `.../email-code`), which redeems them with Orvano and sets both
 * session cookies, so the browser never holds the refresh token. The client then reads the new
 * access cookie and tells its listeners.
 */
export function emailAuthThroughHandler(handlerPath = defaultHandlerPath): EmailAuthTransport {
  const post = async <T>(action: string, body: unknown, options?: RequestOptions): Promise<T> => {
    const init: RequestInit = {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }
    if (options?.signal !== undefined) init.signal = options.signal
    const response = await globalThis.fetch(`${handlerPath}/${action}`, init)
    if (!response.ok) throw await OrvanoError.fromResponse(response)
    return (await response.json()) as T
  }
  return {
    async redeemLink(link, client, options): Promise<LinkResult> {
      const result = await post<HandlerAnswer<LinkResult>>('redeem', link, options)
      if (result.mfaRequired === true) {
        // Only the `orvano_mfa` cookie was set: the sign in waits at the MFA step.
        announce(client, result)
      } else if (link.type === 'magic_link' || link.type === 'recovery')
        await client.reloadSession('signedIn')
      else if ((await client.session.get()) !== null) await client.reloadSession('userUpdated')
      return outcome(result, { type: result.type })
    },
    async signInWithEmailCode(email, code, client, options): Promise<EmailCodeResult> {
      const result = await post<HandlerAnswer<EmailCodeResult>>(
        'email-code',
        { email, code },
        options,
      )
      if (result.mfaRequired === true) announce(client, result)
      else await client.reloadSession('signedIn')
      return outcome(result, {})
    },
  }
}

/**
 * A sign in answer as the route handler sends it: `mfaRequired` may be missing (a handler older
 * than spec 0013), and `expiresAt` comes with a challenge.
 */
type HandlerAnswer<T extends SignInOutcome> = Omit<T, 'mfaRequired'> & {
  mfaRequired?: boolean
  expiresAt?: string
}

/** Tells the client's listeners `mfaRequired` for a sign in the handler's cookie holds. */
function announce(client: Client, result: HandlerAnswer<SignInOutcome>): void {
  client.announceMfa({
    factors: Array.isArray(result.factors) ? result.factors : [],
    expiresAt: typeof result.expiresAt === 'string' ? result.expiresAt : '',
  })
}

/**
 * The browser's {@link MfaTransport} (spec 0013, AC-37): `completeMfa`, `verifyMfa`, and
 * `confirmTotp` post to the app's route handler (`.../mfa`, `.../mfa-verify`, `.../totp-confirm`),
 * which reads the `orvano_mfa` cookie or the session cookies, calls Orvano, and sets the cookies
 * (after `verifyMfa` and `confirmTotp`, only the access cookie: the refresh cookie stays as it
 * was), so the browser never holds a refresh token or a ticket. Passkeys run in the browser; their
 * challenges come from `.../mfa-passkey` and `.../passkey-challenge`, and a passkey sign in
 * finishes at `.../passkey`.
 */
export function mfaThroughHandler(handlerPath = defaultHandlerPath): MfaTransport {
  const post = async (
    action: string,
    body: unknown,
    options?: RequestOptions,
  ): Promise<Response> => {
    const init: RequestInit = {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }
    if (options?.signal !== undefined) init.signal = options.signal
    const response = await globalThis.fetch(`${handlerPath}/${action}`, init)
    if (!response.ok) throw await OrvanoError.fromResponse(response)
    return response
  }
  return {
    async completeMfa(answer, client, options): Promise<SignInOutcome> {
      // A passkey's answer goes flat, as `{ challengeId, credential }` (AC-37).
      await post('mfa', 'passkey' in answer ? answer.passkey : answer, options)
      await client.reloadSession('signedIn')
      return { user: null, isNewUser: false, mfaRequired: false, factors: [] }
    },
    async verifyMfa(answer, client, options): Promise<void> {
      await post('mfa-verify', answer, options)
      await client.reloadSession('tokenRefreshed')
    },
    async confirmTotp(code, client, options): Promise<string[]> {
      const response = await post('totp-confirm', { code }, options)
      const { recoveryCodes } = (await response.json()) as { recoveryCodes: string[] }
      await client.reloadSession('tokenRefreshed')
      return recoveryCodes
    },
    async createMfaPasskeyChallenge(_client, options): Promise<PasskeyChallenge> {
      return (await (await post('mfa-passkey', {}, options)).json()) as PasskeyChallenge
    },
    async createPasskeyChallenge(_client, options): Promise<PasskeyChallenge> {
      return (await (await post('passkey-challenge', {}, options)).json()) as PasskeyChallenge
    },
    async signInWithPasskey(answer, client, options): Promise<SignInOutcome> {
      const response = await post('passkey', answer, options)
      const { user } = (await response.json()) as { user: SignInOutcome['user'] }
      await client.reloadSession('signedIn')
      return { user, isNewUser: false, mfaRequired: false, factors: [] }
    },
  }
}

/** A handler's sign in answer as a `SignInOutcome`, read field by field. */
function outcome<T extends object>(
  result: HandlerAnswer<SignInOutcome>,
  extra: T,
): SignInOutcome & T {
  return {
    ...extra,
    user: result.user,
    isNewUser: result.isNewUser,
    mfaRequired: result.mfaRequired === true,
    factors: Array.isArray(result.factors) ? result.factors : [],
  }
}

/** The `HttpOnly` cookie that holds a provider flow's PKCE verifier, `next`, and purpose (spec 0012, AC-21). */
export const oauthCookie = 'orvano_oauth'

/**
 * The browser client's {@link OAuthTransport} (spec 0012, AC-21): `signInWithOAuth` and
 * `linkIdentity` post `{ provider, next, link, password? }` to the app's route handler
 * (`.../oauth`; `password` is the user's current password, for a link only), which
 * keeps the verifier in an `HttpOnly` cookie and answers the provider's URL, and the browser goes
 * there. The provider comes back to the handler's `.../oauth-callback`, which sets the session
 * cookies and redirects to `next`: the `redirectUrl` option, a path in the app. Both helpers
 * resolve null, since the page navigates away.
 */
export function oauthThroughHandler(handlerPath = defaultHandlerPath): OAuthTransport {
  return {
    async start(purpose, provider, options): Promise<null> {
      const init: RequestInit = {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          provider,
          next: options.redirectUrl,
          link: purpose === 'oauth_link',
          ...(purpose === 'oauth_link' && options.password !== undefined
            ? { password: options.password }
            : {}),
        }),
      }
      if (options.signal !== undefined) init.signal = options.signal
      const response = await globalThis.fetch(`${handlerPath}/oauth`, init)
      if (!response.ok) throw await OrvanoError.fromResponse(response)
      const { url } = (await response.json()) as { url: string }
      if (options.open !== undefined) await options.open(url)
      else (globalThis as { location?: { assign(url: string): void } }).location?.assign(url)
      return null
    },
    redeem(): Promise<never> {
      return Promise.reject(
        new TypeError(
          'Orvano: in Next.js the route handler redeems provider redirects at .../oauth-callback.',
        ),
      )
    },
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
 * `app/api/orvano/[...orvano]/route.ts`) when under a minute of it is left. Its
 * `client.redeemLink`, `client.signInWithEmailCode`, `client.completeMfa`, `client.verifyMfa`, and
 * `client.confirmTotp` go through that handler too, so the session lands in the cookies.
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
        emailAuth: emailAuthThroughHandler(handlerPath),
        oauth: oauthThroughHandler(handlerPath),
        mfa: mfaThroughHandler(handlerPath),
      }),
    )
    browserClients.set(key, orvano)
  }
  return orvano
}
