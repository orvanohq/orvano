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
  MemoryPendingMfaStore,
  MemorySessionStore,
  OrvanoError,
  createPkce,
  directEmailAuth,
  emailLinkTypes,
  refreshMarginMs,
  refreshWithToken,
} from '@orvano/js'
import type {
  AuthSession,
  ClientConfig,
  EmailLink,
  EmailLinkType,
  MfaWireAnswer,
  PasskeyAnswer,
  PendingMfaStore,
  RaisedSession,
  TotpConfirmation,
} from '@orvano/js'
import { NextResponse } from 'next/server.js'
import type { NextRequest } from 'next/server.js'

import {
  decodeJsonCookie,
  decodeMfaCookie,
  encodeJsonCookie,
  encodeMfaCookie,
} from './cookie-codec.js'
import {
  accessClaims,
  accessCookie,
  cookieOptions,
  forwardedClientHeaders,
  mfaCookie,
  mfaCookieOptions,
  oauthCookie,
  refreshCookie,
  secureCookies,
} from './index.js'

/** Settings for {@link updateSession} and {@link createOrvanoRouteHandler}. */
export type OrvanoNextConfig = Omit<ClientConfig, 'session' | 'refresh' | 'mfaStore' | 'mfa'>

/** Settings for {@link createOrvanoRouteHandler}. */
export interface OrvanoRouteHandlerConfig extends OrvanoNextConfig {
  /**
   * The app page that asks for the second factor (spec 0013, AC-37): `GET .../oauth-callback`
   * redirects there when the sign in stops at the MFA step. Defaults to `/sign-in/mfa`.
   */
  mfaPath?: string
}

/** Where the provider callback sends a sign in that stopped at the MFA step, unless told otherwise. */
export const defaultMfaPath = '/sign-in/mfa'

/**
 * A client that speaks for the request's browser: its IP and user agent go along, no session. A
 * sign in that stops at the MFA step leaves its ticket in `mfaStore`.
 */
function clientFor(
  config: OrvanoNextConfig,
  request: NextRequest,
  mfaStore: PendingMfaStore = new MemoryPendingMfaStore(),
): Client {
  return new Client({
    ...config,
    headers: { ...config.headers, ...forwardedClientHeaders(request.headers) },
    session: new MemorySessionStore(),
    mfaStore,
  })
}

/**
 * Keeps a sign in that stopped at the MFA step in the `orvano_mfa` cookie (spec 0013, AC-37), with
 * the app path to land on once it finishes. The ticket never goes in a body.
 */
function writeMfaCookie(
  response: NextResponse,
  store: PendingMfaStore,
  next: string,
  secure: boolean,
): void {
  const pending = store.get()
  if (pending === null) return
  response.cookies.set(mfaCookie, encodeMfaCookie({ ...pending, next }), mfaCookieOptions(secure))
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
 * access token carries the new claim. Answers `{ type, user, isNewUser, mfaRequired, factors }`,
 * never a token or an MFA ticket; a sign in that stopped at the MFA step sets only the `HttpOnly`
 * `orvano_mfa` cookie (spec 0013, AC-37), with `next` from the body.
 */
async function redeem(
  request: NextRequest,
  config: OrvanoNextConfig,
  secure: boolean,
): Promise<NextResponse> {
  const mfaStore = new MemoryPendingMfaStore()
  const client = clientFor(config, request, mfaStore)
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
    mfaRequired: result.mfaRequired,
    factors: result.factors,
    ...mfaExpiry(mfaStore),
  })
  if (result.mfaRequired) {
    writeMfaCookie(response, mfaStore, safeNext(body.next), secure)
    return response
  }
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

/** The challenge's expiry for a body, never its ticket; nothing when there is no challenge. */
function mfaExpiry(store: PendingMfaStore): { expiresAt?: string } {
  const pending = store.get()
  return pending === null ? {} : { expiresAt: pending.expiresAt }
}

/**
 * `POST .../email-code` (spec 0010, AC-25): signs in with an emailed code and sets both cookies. A
 * sign in that stops at the MFA step sets only `orvano_mfa` (spec 0013, AC-37).
 */
async function emailCode(
  request: NextRequest,
  config: OrvanoNextConfig,
  secure: boolean,
): Promise<NextResponse> {
  const mfaStore = new MemoryPendingMfaStore()
  const client = clientFor(config, request, mfaStore)
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
    mfaRequired: result.mfaRequired,
    factors: result.factors,
    ...mfaExpiry(mfaStore),
  })
  if (result.mfaRequired) {
    writeMfaCookie(response, mfaStore, safeNext(body.next), secure)
    return response
  }
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
  return encodeJsonCookie(value)
}

function decodeCookie(value: string | undefined): OAuthCookie | null {
  const parsed = decodeJsonCookie(value)
  return parsed !== null &&
    typeof parsed.v === 'string' &&
    typeof parsed.n === 'string' &&
    (parsed.t === 'oauth' || parsed.t === 'oauth_link')
    ? { v: parsed.v, n: parsed.n, t: parsed.t }
    : null
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
 * `POST .../oauth` (spec 0012, AC-21): `{ provider, next?, link?, password? }`. Makes the PKCE
 * verifier, keeps it with `next` and the purpose in the `orvano_oauth` cookie, starts the flow with
 * Orvano as the browser (for a link, as the signed in user, sending `password`, the user's current
 * password, spec 0013), with the handler's own `.../oauth-callback` as the redirect URL, and
 * answers `{ url }`. The password is never stored.
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
    return problem(400, 'invalid_request', 'Send { provider, next?, link?, password? }.')
  const link = body.link === true
  const password = link && typeof body.password === 'string' ? body.password : undefined
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
      body: {
        provider,
        redirectUrl,
        codeChallenge: challenge,
        ...(password === undefined ? {} : { password }),
      },
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
 * and answers 303 to `next`. A sign in that stops at the MFA step sets `orvano_mfa` instead and
 * goes to `mfaPath` (spec 0013, AC-37). A provider error, a missing cookie, or a refused redemption
 * goes to `next` with `orvano_error`. It needs no `Origin`: the cookie's verifier binds it to this browser.
 */
async function oauthCallback(
  request: NextRequest,
  config: OrvanoRouteHandlerConfig,
  secure: boolean,
): Promise<NextResponse> {
  const mfaStore = new MemoryPendingMfaStore()
  const client = clientFor(config, request, mfaStore)
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
      if (mfaStore.get() !== null) {
        // Spec 0013, AC-37: the MFA page finishes it; `next` waits in the cookie with the ticket.
        const response = done(
          new URL(safeNext(config.mfaPath ?? defaultMfaPath), request.nextUrl.origin),
        )
        writeMfaCookie(response, mfaStore, next, secure)
        return response
      }
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

/** Whether a value has the shape of a passkey's answer: a `challengeId` and a `credential` object. */
function isPasskeyAnswer(value: unknown): value is PasskeyAnswer {
  const answer = value as { challengeId?: unknown; credential?: unknown } | null | undefined
  return (
    typeof answer === 'object' &&
    answer !== null &&
    typeof answer.challengeId === 'string' &&
    typeof answer.credential === 'object' &&
    answer.credential !== null
  )
}

/**
 * The one factor of an `mfa` or `mfa-verify` body: `{ totpCode }`, `{ recoveryCode }`, or a
 * passkey's `{ challengeId, credential }` (also accepted nested as `{ passkey }`, the
 * `account.verifyMfa` form). Null when it holds none or more than one.
 */
function mfaAnswer(body: Record<string, unknown> | null): MfaWireAnswer | null {
  if (body === null) return null
  const { totpCode, recoveryCode, passkey, challengeId, credential } = body
  const flat =
    challengeId === undefined && credential === undefined ? undefined : { challengeId, credential }
  const given = [totpCode, recoveryCode, passkey, flat].filter((v) => v !== undefined)
  if (given.length !== 1) return null
  if (typeof totpCode === 'string') return { totpCode }
  if (typeof recoveryCode === 'string') return { recoveryCode }
  const answer = passkey ?? flat
  return isPasskeyAnswer(answer) ? { passkey: answer } : null
}

const answerHint = 'Send { totpCode }, { recoveryCode }, or a passkey { challengeId, credential }.'

/** The `orvano_mfa` cookie's ticket, or a 401 `invalid_mfa_ticket` answer when there is none. */
function mfaTicket(
  request: NextRequest,
): NonNullable<ReturnType<typeof decodeMfaCookie>> | NextResponse {
  const cookie = decodeMfaCookie(request.cookies.get(mfaCookie)?.value)
  return (
    cookie ??
    problem(401, 'invalid_mfa_ticket', 'No sign in is waiting for a second factor. Sign in again.')
  )
}

/**
 * `POST .../mfa` (spec 0013, AC-37): `{ totpCode }`, `{ recoveryCode }`, or a passkey's
 * `{ challengeId, credential }` (the challenge from `.../mfa-passkey`). Reads the `orvano_mfa`
 * cookie, finishes the sign in with Orvano as the browser, sets both session cookies, clears
 * `orvano_mfa`, and answers `{ next }`. A missing cookie is 401 `invalid_mfa_ticket`, and an ended
 * ticket clears the cookie.
 */
async function completeMfa(
  request: NextRequest,
  config: OrvanoNextConfig,
  secure: boolean,
): Promise<NextResponse> {
  const answer = mfaAnswer(await jsonBody(request))
  if (answer === null) return problem(400, 'invalid_request', answerHint)
  const cookie = mfaTicket(request)
  if (cookie instanceof NextResponse) return cookie

  const mfaStore = new MemoryPendingMfaStore()
  mfaStore.set({ ticket: cookie.ticket, factors: [], expiresAt: cookie.expiresAt })
  const client = clientFor(config, request, mfaStore)
  try {
    await client.completeMfa(answer)
  } catch (error) {
    if (!(error instanceof OrvanoError)) throw error
    const refused = passThrough(error)
    if (error.code === 'invalid_mfa_ticket') refused.cookies.delete(mfaCookie)
    return refused
  }

  const response = NextResponse.json({ next: safeNext(cookie.next) })
  writeResponse(response, await client.session.get(), secure)
  response.cookies.delete(mfaCookie)
  return response
}

/**
 * `POST .../mfa-passkey` (spec 0013, AC-37): reads the `orvano_mfa` cookie and answers the passkey
 * challenge for its ticket (`account.createMfaPasskeyChallenge`), so the browser can run the
 * ceremony without ever seeing the ticket. An ended ticket clears the cookie.
 */
async function mfaPasskeyChallenge(request: NextRequest, client: Client): Promise<NextResponse> {
  const cookie = mfaTicket(request)
  if (cookie instanceof NextResponse) return cookie
  try {
    return NextResponse.json(
      await client.request<unknown>({
        method: 'POST',
        path: '/v1/account/sessions/mfa/passkey-challenge',
        body: { ticket: cookie.ticket },
      }),
    )
  } catch (error) {
    if (!(error instanceof OrvanoError)) throw error
    const refused = passThrough(error)
    if (error.code === 'invalid_mfa_ticket') refused.cookies.delete(mfaCookie)
    return refused
  }
}

/** `POST .../passkey-challenge` (spec 0013, AC-37): starts a passkey sign in and answers the challenge. */
async function passkeyChallenge(client: Client): Promise<NextResponse> {
  try {
    return NextResponse.json(
      await client.request<unknown>({
        method: 'POST',
        path: '/v1/account/sessions/passkey-challenge',
      }),
    )
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }
}

/**
 * `POST .../passkey` (spec 0013, AC-37): `{ challengeId, credential }`. Signs in with the passkey's
 * answer, sets both session cookies, and answers `{ user, isNewUser }`. A passkey sign in never
 * stops at the MFA step.
 */
async function passkeySignIn(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const body = await jsonBody(request)
  if (!isPasskeyAnswer(body))
    return problem(400, 'invalid_request', 'Send { challengeId, credential }.')
  let result: { user: unknown; isNewUser: boolean }
  try {
    result = await client.request<{ user: unknown; isNewUser: boolean }>({
      method: 'POST',
      path: '/v1/account/sessions/passkey',
      body: { challengeId: body.challengeId, credential: body.credential },
      session: 'start',
    })
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }
  const response = NextResponse.json({ user: result.user, isNewUser: result.isNewUser })
  writeResponse(response, await client.session.get(), secure)
  return response
}

/**
 * The session to write after a second factor (spec 0013): the raised access token, with the
 * refresh token `linkSession` refreshed to when it did (Orvano sends none back), else no refresh
 * token, so `writeResponse` leaves the refresh cookie as it was.
 */
function raised(answer: RaisedSession, fresh: AuthSession | null): AuthSession {
  return {
    accessToken: answer.accessToken,
    accessTokenExpiresAt: answer.accessTokenExpiresAt,
    refreshToken: fresh?.refreshToken ?? null,
    refreshTokenExpiresAt: fresh?.refreshTokenExpiresAt ?? null,
    sessionId: answer.sessionId,
  }
}

/**
 * `POST .../totp-confirm` (spec 0013, AC-37): `{ code }`. Turns MFA on as the signed in user,
 * sets the new access cookie (the refresh cookie stays as it was), and answers
 * `{ recoveryCodes }`.
 */
async function confirmTotp(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const body = await jsonBody(request)
  if (body === null || typeof body.code !== 'string')
    return problem(400, 'invalid_request', 'Send { code }.')
  try {
    const session = await linkSession(request, client)
    if (session === null) return problem(401, 'session_required', 'Sign in first.')
    const confirmation = await client.request<TotpConfirmation>({
      method: 'POST',
      path: '/v1/account/mfa/totp/confirm',
      body: { code: body.code },
      bearer: session.access,
    })
    const response = NextResponse.json({ recoveryCodes: confirmation.recoveryCodes })
    writeResponse(response, raised(confirmation.session, session.fresh), secure)
    return response
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
    throw error
  }
}

/**
 * `POST .../mfa-verify` (spec 0013, AC-37): the `account.verifyMfa` body (`{ totpCode }`,
 * `{ recoveryCode }`, or `{ passkey }`). Step up as the signed in user: sets the new access cookie
 * (the refresh cookie stays as it was) and answers 204.
 */
async function verifyMfa(
  request: NextRequest,
  client: Client,
  secure: boolean,
): Promise<NextResponse> {
  const answer = mfaAnswer(await jsonBody(request))
  if (answer === null) return problem(400, 'invalid_request', answerHint)
  try {
    const session = await linkSession(request, client)
    if (session === null) return problem(401, 'session_required', 'Sign in first.')
    const answered = await client.request<RaisedSession>({
      method: 'POST',
      path: '/v1/account/mfa/verify',
      body: answer,
      bearer: session.access,
    })
    const response = new NextResponse(null, { status: 204 })
    writeResponse(response, raised(answered, session.fresh), secure)
    return response
  } catch (error) {
    if (error instanceof OrvanoError) return passThrough(error)
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
 *
 * MFA (spec 0013, AC-37): a sign in that stops at the MFA step sets the `HttpOnly` `orvano_mfa`
 * cookie (`redeem` and `email-code` answer `{ mfaRequired: true, factors, expiresAt }`; the
 * provider callback redirects to `mfaPath`). `POST .../mfa` finishes it and sets both session
 * cookies; `POST .../totp-confirm` turns MFA on and `POST .../mfa-verify` steps up, each setting
 * the new access cookie and leaving the refresh cookie as it was (spec 0013). Passkeys:
 * `POST .../mfa-passkey` answers the challenge for the waiting sign in, `POST .../passkey-challenge`
 * starts a passkey sign in, and `POST .../passkey` finishes it and sets the cookies.
 */
export function createOrvanoRouteHandler(config: OrvanoRouteHandlerConfig): {
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
      return oauthCallback(request, config, secureCookies(request.nextUrl.origin))
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

      if (action === 'redeem') return redeem(request, config, secure)
      if (action === 'email-code') return emailCode(request, config, secure)
      if (action === 'oauth') return oauthStart(request, client, secure)
      if (action === 'mfa') return completeMfa(request, config, secure)
      if (action === 'totp-confirm') return confirmTotp(request, client, secure)
      if (action === 'mfa-verify') return verifyMfa(request, client, secure)
      if (action === 'mfa-passkey') return mfaPasskeyChallenge(request, client)
      if (action === 'passkey-challenge') return passkeyChallenge(client)
      if (action === 'passkey') return passkeySignIn(request, client, secure)

      return problem(404, 'not_found', 'Unknown Orvano action.')
    },
  }
}
