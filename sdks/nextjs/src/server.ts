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

/**
 * The route handler the browser client refreshes, signs out, and redeems emailed links and codes
 * through. Mount it once, at `app/api/orvano/[...orvano]/route.ts`: `POST .../refresh` trades the
 * `HttpOnly` refresh cookie and sets both cookies again, answering only the access token;
 * `POST .../signout` ends the session and clears both cookies; `POST .../redeem`
 * (`{ type, token, password? }`) and `POST .../email-code` (`{ email, code }`) redeem with Orvano
 * and set both cookies, answering `{ type, user, isNewUser }`. A request whose `Origin` is missing
 * or is not the app's own gets 403 before any cookie is read. There is no GET action, so a mail
 * scanner that opens a link never uses it up.
 */
export function createOrvanoRouteHandler(config: OrvanoNextConfig): {
  POST: (request: NextRequest) => Promise<NextResponse>
} {
  return {
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

      return problem(404, 'not_found', 'Unknown Orvano action.')
    },
  }
}
