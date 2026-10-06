/**
 * Server only helpers for Next.js: `updateSession` for middleware and the route handler that
 * refreshes, signs out, and redeems emailed links and codes for the browser client (spec 0004,
 * AC-23; spec 0010, AC-25).
 *
 * @example
 * ```ts
 * // middleware.ts
 * import type { NextRequest } from 'next/server.js'
 * import { updateSession } from '@orvano/nextjs/server'
 *
 * export function middleware(request: NextRequest) {
 *   return updateSession(request, { endpoint: process.env.ORVANO_ENDPOINT!, project: 'my-project' })
 * }
 *
 * // app/api/orvano/[...orvano]/route.ts
 * import { createOrvanoRouteHandler } from '@orvano/nextjs/server'
 *
 * export const { POST } = createOrvanoRouteHandler({ endpoint: process.env.ORVANO_ENDPOINT!, project: 'my-project' })
 * ```
 *
 * @packageDocumentation
 */
import {
  Client,
  MemorySessionStore,
  OrvanoError,
  createPkce,
  directEmailAuth,
  emailLinkTypes,
  refreshMarginMs,
  refreshWithToken,
} from '@orvano/js'
import type { AuthSession, ClientConfig, EmailLink, EmailLinkType } from '@orvano/js'
import { NextResponse } from 'next/server.js'
import type { NextRequest } from 'next/server.js'

import {
  accessClaims,
  accessCookie,
  cookieOptions,
  forwardedClientHeaders,
  oauthCookie,
  refreshCookie,
  secureCookies,
} from './index.js'

/** Settings for {@link updateSession} and {@link createOrvanoRouteHandler}. */
export type OrvanoNextConfig = Omit<ClientConfig, 'session' | 'refresh'>

/** A client that speaks for the request's browser: its IP and user agent go along, no session. */
function clientFor(config: OrvanoNextConfig, request: NextRequest): Client {
  return new Client({
    ...config,
    headers: { ...config.headers, ...forwardedClientHeaders(request.headers) },
    session: new MemorySessionStore(),
  })
}

/**
 * Trades the refresh cookie for a new session. Returns null when Orvano refuses it (the session
 * is over), and throws on anything else (a network error keeps the cookies as they are).
 */
async function refreshWith(client: Client, refreshToken: string): Promise<AuthSession | null> {
  try {
    return await refreshWithToken(
      {
        accessToken: '',
        accessTokenExpiresAt: '',
        refreshToken,
        refreshTokenExpiresAt: null,
        sessionId: '',
      },
      client,
    )
  } catch (error) {
    if (error instanceof OrvanoError && error.status === 401) return null
    throw error
  }
}

/** Puts the session in the request's cookies, so the rest of this request sees it. */
function writeRequest(request: NextRequest, session: AuthSession | null): void {
  if (session === null) {
    request.cookies.delete(accessCookie)
    request.cookies.delete(refreshCookie)
    return
  }
  request.cookies.set(accessCookie, session.accessToken)
  if (session.refreshToken !== null) request.cookies.set(refreshCookie, session.refreshToken)
}

/** Sets or clears both session cookies on the response. */
function writeResponse(response: NextResponse, session: AuthSession | null, secure: boolean): void {
  if (session === null) {
    response.cookies.delete(accessCookie)
    response.cookies.delete(refreshCookie)
    return
  }
  response.cookies.set(
    accessCookie,
    session.accessToken,
    cookieOptions(false, secure, new Date(session.accessTokenExpiresAt)),
  )
  if (session.refreshToken !== null && session.refreshTokenExpiresAt !== null) {
    response.cookies.set(
      refreshCookie,
      session.refreshToken,
      cookieOptions(true, secure, new Date(session.refreshTokenExpiresAt)),
    )
  }
}

/**
 * For middleware: when under a minute is left of the access token (or it is gone and a refresh
 * cookie remains), refreshes the session and sets both cookies on the request (for the rest of
 * this request) and on the response. A refused refresh clears both; a network error changes
 * nothing. Return its response from your middleware.
 */
export async function updateSession(
  request: NextRequest,
  config: OrvanoNextConfig,
): Promise<NextResponse> {
  const refreshToken = request.cookies.get(refreshCookie)?.value
  const access = request.cookies.get(accessCookie)?.value
  const claims = access === undefined ? null : accessClaims(access)
  const fresh = claims !== null && claims.exp * 1000 - Date.now() >= refreshMarginMs
  if (fresh || refreshToken === undefined || refreshToken === '')
    return NextResponse.next({ request })

  let session: AuthSession | null
  try {
    session = await refreshWith(clientFor(config, request), refreshToken)
  } catch {
    return NextResponse.next({ request })
  }

  const secure = secureCookies(request.nextUrl.origin)
  writeRequest(request, session)
  const response = NextResponse.next({ request })
  writeResponse(response, session, secure)
  return response
}

function problem(status: number, code: string, detail: string): NextResponse {
  return NextResponse.json(
    { type: 'about:blank', title: detail, status, code, detail },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  )
}

/** The reason phrases of the statuses Orvano's email flows answer with. */
const reasons: Record<number, string> = {
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  409: 'Conflict',
  429: 'Too Many Requests',
  503: 'Service Unavailable',
}

/** Orvano's refusal, passed through with its status, code, detail, request ID, and `Retry-After`. */
function passThrough(error: OrvanoError): NextResponse {
  const response = NextResponse.json(
    {
      type: `https://orvano.dev/errors/${error.code}`,
      title: reasons[error.status] ?? 'Error',
      status: error.status,
      detail: error.message,
      code: error.code,
      ...(error.requestId === null ? {} : { requestId: error.requestId }),
    },
    { status: error.status, headers: { 'Content-Type': 'application/problem+json' } },
  )
  if (error.retryAfter !== null) response.headers.set('Retry-After', String(error.retryAfter))
  return response
}

/** The JSON object body of a request, or null when it is not one. */
async function jsonBody(request: NextRequest): Promise<Record<string, unknown> | null> {
  try {
    const body: unknown = await request.json()
    return typeof body === 'object' && body !== null && !Array.isArray(body)
      ? (body as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

function isLinkType(value: unknown): value is EmailLinkType {
  return typeof value === 'string' && (emailLinkTypes as readonly string[]).includes(value)
}

/**
 * `POST .../redeem` (spec 0010, AC-25): redeems a link with Orvano as the browser. A magic link or
 * reset sets both cookies; a verification or email change refreshes them when they exist, so the
 * access token carries the new claim. Answers `{ type, user, isNewUser }`, never a token.
 */
async function redeem(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const body = await jsonBody(request)
  if (body === null || !isLinkType(body.type) || typeof body.token !== 'string')
    return problem(400, 'invalid_request', 'Send { type, token } and, for a reset, password.')
  const link: EmailLink = { type: body.type, token: body.token }
  if (typeof body.password === 'string') link.password = body.password

  let result
  try {
    result = await directEmailAuth.redeemLink(link, client)
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }

  const response = NextResponse.json({
    type: result.type,
    user: result.user,
    isNewUser: result.isNewUser,
  })
  if (link.type === 'magic_link' || link.type === 'recovery') {
    writeResponse(response, await client.session.get(), secure)
    return response
  }
  const refreshToken = request.cookies.get(refreshCookie)?.value
  if (refreshToken !== undefined && refreshToken !== '') {
    try {
      const session = await refreshWith(client, refreshToken)
      writeResponse(response, session, secure)
    } catch {
      // Orvano is unreachable: the cookies stay, and the claim updates at the next refresh.
    }
  }
  return response
}

/** `POST .../email-code` (spec 0010, AC-25): signs in with an emailed code and sets both cookies. */
async function emailCode(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const body = await jsonBody(request)
  if (body === null || typeof body.email !== 'string' || typeof body.code !== 'string')
    return problem(400, 'invalid_request', 'Send { email, code }.')

  let result
  try {
    result = await directEmailAuth.signInWithEmailCode(body.email, body.code, client)
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }

  const response = NextResponse.json({
    type: 'email_code',
    user: result.user,
    isNewUser: result.isNewUser,
  })
  writeResponse(response, await client.session.get(), secure)
  return response
}

/** How long the `orvano_oauth` cookie lives: the flow's 10 minutes plus the code's 2 (AC-21). */
const oauthCookieSeconds = 720

const oauthProviders = ['google', 'apple', 'github', 'microsoft'] as const

/** What the `orvano_oauth` cookie holds while a provider flow runs. */
interface OAuthCookie {
  /** The PKCE verifier. */
  v: string
  /** The app path to land on. */
  n: string
  /** `oauth` or `oauth_link`. */
  t: 'oauth' | 'oauth_link'
}

/**
 * A path in the app: one leading `/`, no control characters or backslashes (a browser strips tabs and newlines and
 * reads `\` as `/`, so `/\t/evil.com` is another host), and it must stay on the same origin once parsed.
 */
export function safeNext(next: unknown): string {
  if (typeof next !== 'string' || !next.startsWith('/') || next.startsWith('//')) return '/'
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f\\]/.test(next)) return '/'
  try {
    const base = 'http://orvano.invalid'
    const parsed = new URL(next, base)
    return parsed.origin === base ? parsed.pathname + parsed.search + parsed.hash : '/'
  } catch {
    return '/'
  }
}

function encodeCookie(value: OAuthCookie): string {
  // UTF-8 first: btoa throws on any character above U+00FF, such as a `next` of `/日本`.
  let binary = ''
  for (const byte of new TextEncoder().encode(JSON.stringify(value)))
    binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function decodeCookie(value: string | undefined): OAuthCookie | null {
  if (value === undefined || value === '') return null
  try {
    const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/'))
    const parsed = JSON.parse(
      new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0))),
    ) as Partial<OAuthCookie>
    return typeof parsed.v === 'string' &&
      typeof parsed.n === 'string' &&
      (parsed.t === 'oauth' || parsed.t === 'oauth_link')
      ? { v: parsed.v, n: parsed.n, t: parsed.t }
      : null
  } catch {
    return null
  }
}

/** The handler's own base path: the request path without its last segment (the action). */
function handlerBase(request: NextRequest): string {
  return request.nextUrl.pathname.replace(/\/[^/]*\/?$/, '')
}

/**
 * The signed in user's access token for a link (AC-21): the access cookie, or a fresh session from
 * the refresh cookie when it is missing or under a minute from expiring, since a link flow can
 * outlast the 15 minute access token. Null when nobody is signed in.
 */
async function linkSession(
  request: NextRequest,
  client: Client,
): Promise<{ access: string; fresh: AuthSession | null } | null> {
  const access = request.cookies.get(accessCookie)?.value
  const claims = access === undefined ? null : accessClaims(access)
  if (access !== undefined && claims !== null && claims.exp * 1000 - Date.now() > refreshMarginMs)
    return { access, fresh: null }
  const refreshToken = request.cookies.get(refreshCookie)?.value
  if (refreshToken === undefined || refreshToken === '') return null
  const fresh = await refreshWith(client, refreshToken)
  return fresh === null ? null : { access: fresh.accessToken, fresh }
}

/**
 * `POST .../oauth` (spec 0012, AC-21): `{ provider, next?, link? }`. Makes the PKCE verifier, keeps
 * it with `next` and the purpose in the `orvano_oauth` cookie, starts the flow with Orvano as the
 * browser (for a link, as the signed in user), with the handler's own `.../oauth-callback` as the
 * redirect URL, and answers `{ url }`.
 */
async function oauthStart(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const body = await jsonBody(request)
  const provider = body?.provider
  if (
    body === null ||
    typeof provider !== 'string' ||
    !(oauthProviders as readonly string[]).includes(provider)
  )
    return problem(400, 'invalid_request', 'Send { provider, next?, link? }.')
  const link = body.link === true
  const { verifier, challenge } = await createPkce()
  const redirectUrl = `${request.nextUrl.origin}${handlerBase(request)}/oauth-callback`

  let session: { access: string; fresh: AuthSession | null } | null = null
  let url: string
  try {
    if (link) {
      session = await linkSession(request, client)
      if (session === null) return problem(401, 'session_required', 'Sign in first.')
    }
    const flow = await client.request<{ url: string }>({
      method: 'POST',
      path: link ? '/v1/account/identities/oauth/flows' : '/v1/account/oauth/flows',
      body: { provider, redirectUrl, codeChallenge: challenge },
      ...(session === null ? {} : { bearer: session.access }),
    })
    url = flow.url
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }

  const response = NextResponse.json({ url })
  if (session?.fresh != null) writeResponse(response, session.fresh, secure)
  response.cookies.set(
    oauthCookie,
    encodeCookie({ v: verifier, n: safeNext(body.next), t: link ? 'oauth_link' : 'oauth' }),
    {
      httpOnly: true,
      secure,
      sameSite: 'lax',
      path: '/',
      maxAge: oauthCookieSeconds,
    },
  )
  return response
}

/**
 * `GET .../oauth-callback` (spec 0012, AC-21): where the provider flow comes back. Reads the
 * `orvano_oauth` cookie, redeems `orvano_code` with its verifier, sets both session cookies (for a
 * link, refreshing first when the access cookie is missing or nearly expired), clears the cookie,
 * and answers 303 to `next`. A provider error, a missing cookie, or a refused redemption goes to
 * `next` with `orvano_error`. It needs no `Origin`: the cookie's verifier binds it to this browser.
 */
async function oauthCallback(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const state = decodeCookie(request.cookies.get(oauthCookie)?.value)
  const next = safeNext(state?.n)
  const target = (error?: string): URL => {
    const url = new URL(next, request.nextUrl.origin)
    if (error !== undefined) url.searchParams.set('orvano_error', error)
    return url
  }
  const done = (url: URL, session?: AuthSession | null): NextResponse => {
    const response = NextResponse.redirect(url, 303)
    response.headers.set('Cache-Control', 'no-store')
    response.headers.set('Referrer-Policy', 'no-referrer')
    if (session !== undefined && session !== null) writeResponse(response, session, secure)
    response.cookies.delete(oauthCookie)
    return response
  }

  const params = request.nextUrl.searchParams
  const providerError = params.get('orvano_error')
  if (providerError !== null) return done(target(providerError))
  const code = params.get('orvano_code')
  if (state === null || code === null || code === '') return done(target('invalid_oauth_code'))

  try {
    if (state.t === 'oauth') {
      await client.request({
        method: 'POST',
        path: '/v1/account/sessions/oauth',
        body: { code, codeVerifier: state.v },
        session: 'start',
      })
      return done(target(), await client.session.get())
    }

    const session = await linkSession(request, client)
    if (session === null) return done(target('session_required'))
    await client.request({
      method: 'POST',
      path: '/v1/account/identities/oauth',
      body: { code, codeVerifier: state.v },
      bearer: session.access,
    })
    return done(target(), session.fresh)
  } catch (error) {
    if (error instanceof OrvanoError) return done(target(error.code))
    throw error
  }
}

/**
 * The route handler the browser client refreshes, signs out, and redeems emailed links and codes
 * through. Mount it once, at `app/api/orvano/[...orvano]/route.ts`: `POST .../refresh` trades the
 * `HttpOnly` refresh cookie and sets both cookies again, answering only the access token;
 * `POST .../signout` ends the session and clears both cookies; `POST .../redeem`
 * (`{ type, token, password? }`) and `POST .../email-code` (`{ email, code }`) redeem with Orvano
 * and set both cookies, answering `{ type, user, isNewUser }`. A request whose `Origin` is missing
 * or is not the app's own gets 403 before any cookie is read. There is no GET action, so a mail
 * scanner that opens a link never uses it up.
 *
 * Provider sign in (spec 0012, AC-21): `POST .../oauth` (`{ provider, next?, link? }`) starts a flow
 * and answers `{ url }`, and `GET .../oauth-callback` finishes it and redirects to `next`. Export
 * both methods: `export const { GET, POST } = createOrvanoRouteHandler(...)`.
 */
export function createOrvanoRouteHandler(config: OrvanoNextConfig): {
  GET: (request: NextRequest) => Promise<NextResponse>
  POST: (request: NextRequest) => Promise<NextResponse>
} {
  return {
    async GET(request: NextRequest): Promise<NextResponse> {
      const action = request.nextUrl.pathname
        .split('/')
        .filter((part: string) => part !== '')
        .at(-1)
      if (action !== 'oauth-callback') return problem(404, 'not_found', 'Unknown Orvano action.')
      return oauthCallback(
        request,
        clientFor(config, request),
        secureCookies(request.nextUrl.origin),
      )
    },

    async POST(request: NextRequest): Promise<NextResponse> {
      if (request.headers.get('origin') !== request.nextUrl.origin)
        return problem(403, 'origin_not_allowed', 'This handler only answers the app itself.')

      const action = request.nextUrl.pathname
        .split('/')
        .filter((part: string) => part !== '')
        .at(-1)
      const secure = secureCookies(request.nextUrl.origin)
      const client = clientFor(config, request)

      if (action === 'refresh') {
        const refreshToken = request.cookies.get(refreshCookie)?.value
        let session: AuthSession | null = null
        if (refreshToken !== undefined && refreshToken !== '') {
          try {
            session = await refreshWith(client, refreshToken)
          } catch {
            return problem(503, 'server_busy', 'Orvano could not be reached. Try again.')
          }
        }
        if (session === null) {
          const refused = problem(
            401,
            'invalid_refresh_token',
            'The session is over. Sign in again.',
          )
          writeResponse(refused, null, secure)
          return refused
        }
        const response = NextResponse.json({
          accessToken: session.accessToken,
          accessTokenExpiresAt: session.accessTokenExpiresAt,
          sessionId: session.sessionId,
        })
        writeResponse(response, session, secure)
        return response
      }

      if (action === 'signout') {
        const access = request.cookies.get(accessCookie)?.value
        if (access !== undefined && access !== '') {
          try {
            await client.request({
              method: 'DELETE',
              path: '/v1/account/sessions/current',
              bearer: access,
            })
          } catch {
            // Already ended, or Orvano is unreachable: the cookies go either way.
          }
        }
        const response = new NextResponse(null, { status: 204 })
        writeResponse(response, null, secure)
        return response
      }

      if (action === 'redeem') return redeem(request, client, secure)
      if (action === 'email-code') return emailCode(request, client, secure)
      if (action === 'oauth') return oauthStart(request, client, secure)

      return problem(404, 'not_found', 'Unknown Orvano action.')
    },
  }
}
