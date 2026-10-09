import {
  LocalStorageSessionStore,
  MemorySessionStore,
  authorizationHeader,
  hasLocalStorage,
  raisedSessionFrom,
  readAccessClaims,
  sessionFrom,
} from './auth.js'
import type { AuthEvent, AuthSession, AuthStateListener, SessionStore } from './auth.js'
import { OrvanoError } from './error.js'
import { directEmailAuth, readEmailLink, removeLinkFromAddressBar } from './links.js'
import type { EmailAuthTransport, EmailCodeResult, LinkResult, RedeemLinkOptions } from './links.js'
import {
  clearVerifier,
  directOAuth,
  linkIdentityWithIdToken,
  oauthRedirectError,
  readOAuthRedirect,
  removeOAuthFromAddressBar,
  signInWithIdToken,
} from './oauth.js'
import type {
  IdTokenCredentials,
  IdTokenSignInResult,
  IdentityLinkOptions,
  IdentityLinkResult,
  LinkIdentityOptions,
  OAuthOptions,
  OAuthSignInResult,
  OAuthTransport,
} from './oauth.js'
import { MemoryPendingMfaStore, signInOutcome } from './mfa.js'
import type {
  MfaAnswer,
  MfaTransport,
  MfaWireAnswer,
  PendingMfa,
  PendingMfaStore,
  PendingMfaTicket,
  SignInOutcome,
} from './mfa.js'
import { browserPasskeys } from './passkeys.js'
import type {
  PasskeyAuthenticator,
  PasskeyRegistrationOptions,
  PasskeySignInOptions,
} from './passkeys.js'
import { sdkHeader, sdkName, serverVersionHeader, versionMismatch } from './version.js'
import { AccountService } from '../generated/client.js'
import type {
  Identity,
  MfaFactor,
  OAuthProvider,
  Passkey,
  PasskeyChallenge,
} from '../generated/models.js'
import type { Logger } from './version.js'
import { sdkVersion } from '../generated/version.js'

/** Settings for a {@link Client}. */
export interface ClientConfig {
  /** Your Orvano server's base URL, for example `https://orvano.example.com`. */
  endpoint: string
  /** The project ID, sent as `X-Orvano-Project` on every call. */
  project?: string
  /** Extra headers sent with every request. */
  headers?: Record<string, string>
  /**
   * Where the signed in user's session lives. Defaults to `localStorage` under
   * `orvano.session.<project>` in a browser, and to memory elsewhere.
   */
  session?: SessionStore
  /**
   * How the client trades the session for a fresh one. Defaults to `account.refreshSession` with
   * the stored refresh token; `@orvano/nextjs` asks the app's route handler instead.
   */
  refresh?: SessionRefresher
  /** How long one call may take, retries included, in milliseconds. Defaults to 30000; 0 turns it off. */
  timeoutMs?: number
  /** How many times a safe call is retried after a 429 or 503. Defaults to 3. */
  maxRetries?: number
  /**
   * How emailed links and codes are redeemed (spec 0010). Defaults to the matching `account`
   * operation; `@orvano/nextjs` posts them to the app's route handler instead.
   */
  emailAuth?: EmailAuthTransport
  /**
   * How provider sign in flows start and finish (spec 0012). Defaults to the `account` operations
   * with the PKCE verifier in `sessionStorage`; `@orvano/nextjs` posts to the app's route handler.
   */
  oauth?: OAuthTransport
  /**
   * Where a sign in waiting at the MFA step keeps its ticket (spec 0013). Defaults to this
   * client's memory; `@orvano/nextjs` keeps it in an `HttpOnly` cookie on the server.
   */
  mfaStore?: PendingMfaStore
  /**
   * How `completeMfa`, `verifyMfa`, and `confirmTotp` run (spec 0013). Defaults to the `account`
   * operations; `@orvano/nextjs`'s browser client posts them to the app's route handler.
   */
  mfa?: MfaTransport
  /**
   * Makes and uses passkeys (spec 0013). Defaults to the browser's WebAuthn API
   * (`browserPasskeys`); pass your own for a native bridge or a test authenticator.
   */
  passkeys?: PasskeyAuthenticator
  /** A custom `fetch`, for tests or runtimes without a global one. */
  fetch?: typeof fetch
  /**
   * Where warnings go, for example the one logged when the server's major.minor differs from
   * this SDK's. Defaults to `console`.
   */
  logger?: Logger
}

/** Per call settings, the last argument of every generated method. */
export interface RequestOptions {
  /** Cancels the call. */
  signal?: AbortSignal
  /** Overrides the client's timeout for this call, in milliseconds; 0 turns it off. */
  timeoutMs?: number
}

/** The HTTP methods the contract uses. */
export type HttpMethod = 'GET' | 'HEAD' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

/** One HTTP call, as a generated service describes it. */
export interface RequestSpec {
  /** The HTTP method. */
  method: HttpMethod
  /** The path under the endpoint, starting with `/v1/`. */
  path: string
  /** Query parameters; `undefined` values are left out. */
  query?: Record<string, string | number | boolean | undefined> | undefined
  /** The JSON body. */
  body?: unknown
  /** Marked `x-orvano-idempotent` in the contract, so it is safe to retry. */
  idempotent?: boolean
  /**
   * Marked `x-orvano-session` in the contract: on success, `start` stores the response's
   * `session`, `refresh` stores the response itself, `end` clears the stored session, and `user`
   * tells the listeners the user changed.
   */
  session?: 'start' | 'refresh' | 'end' | 'user'
  /** Sends no user credentials and never refreshes first: the refresh call itself. */
  anonymous?: boolean
  /**
   * An access token to send as `Authorization: Bearer` instead of this client's own credentials,
   * so a call made as that user never carries an API key.
   */
  bearer?: string
  /** Sends `Cache-Control: no-cache`, so no cache in between answers from before a change. */
  noCache?: boolean
}

/**
 * How a client trades a session for a fresh one. Throws an {@link OrvanoError} with status 401
 * when the session is over; any other error (a network error, a timeout) keeps the session.
 */
export type SessionRefresher = (session: AuthSession, client: Client) => Promise<AuthSession>

/** A client refreshes before a call when less than this is left of the access token (AC-26). */
export const refreshMarginMs = 60_000

/** The default {@link SessionRefresher}: `account.refreshSession` with the stored refresh token. */
export const refreshWithToken: SessionRefresher = async (session, client) => {
  if (session.refreshToken === null)
    throw new OrvanoError(401, 'invalid_refresh_token', 'This client holds no refresh token.', null)
  const tokens = await client.request<unknown>({
    method: 'POST',
    path: '/v1/account/sessions/refresh',
    body: { refreshToken: session.refreshToken },
    idempotent: true,
    anonymous: true,
  })
  return sessionFrom(tokens)
}

const defaultTimeoutMs = 30_000
const defaultMaxRetries = 3
const backoffBaseMs = 250

/**
 * Sends requests to one Orvano server. Generated services (`orvano.health`, ...) all send
 * through a `Client`. It retries safe calls (GET, HEAD, and operations marked idempotent) on 429
 * and 503, honoring `Retry-After`, and gives every call a timeout. When someone is signed in it
 * sends their access token, refreshes it before a call when under a minute is left, and after a
 * 401 for an expired or refused token refreshes once and repeats the call once (spec 0004, AC-26).
 */
export class Client {
  /** Where the signed in user's session lives. */
  readonly session: SessionStore
  readonly #endpoint: string
  readonly #project: string | undefined
  readonly #headers: Record<string, string>
  readonly #timeoutMs: number
  readonly #maxRetries: number
  readonly #fetch: typeof fetch
  readonly #logger: Logger
  readonly #refresher: SessionRefresher
  readonly #emailAuth: EmailAuthTransport
  readonly #oauth: OAuthTransport
  readonly #listeners = new Set<AuthStateListener>()
  #known: AuthSession | null | undefined
  readonly #mfaStore: PendingMfaStore
  readonly #mfaTransport: MfaTransport | undefined
  readonly #passkeys: PasskeyAuthenticator
  #refreshing: Promise<AuthSession | null> | undefined
  #versionChecked = false

  /** The server's base URL, without a trailing slash. */
  get endpoint(): string {
    return this.#endpoint
  }

  /** The project sent as `X-Orvano-Project`, if any. */
  get project(): string | undefined {
    return this.#project
  }

  constructor(config: ClientConfig) {
    let url: URL
    try {
      url = new URL(config.endpoint)
    } catch {
      throw new TypeError(`Orvano endpoint must be an absolute URL, got "${config.endpoint}"`)
    }
    this.#endpoint = url.href.replace(/\/+$/, '')
    this.#project = config.project
    this.#headers = { ...config.headers }
    this.session =
      config.session ??
      (hasLocalStorage() ? new LocalStorageSessionStore(config.project) : new MemorySessionStore())
    this.#refresher = config.refresh ?? refreshWithToken
    this.#emailAuth = config.emailAuth ?? directEmailAuth
    this.#oauth = config.oauth ?? directOAuth
    this.#mfaStore = config.mfaStore ?? new MemoryPendingMfaStore()
    this.#mfaTransport = config.mfa
    this.#passkeys = config.passkeys ?? browserPasskeys
    this.#timeoutMs = config.timeoutMs ?? defaultTimeoutMs
    this.#maxRetries = config.maxRetries ?? defaultMaxRetries
    // Bound, because some runtimes (Cloudflare Workers) reject a fetch called on another `this`.
    this.#fetch = config.fetch ?? globalThis.fetch.bind(globalThis)
    this.#logger = config.logger ?? console
    if (this.session.subscribe !== undefined) {
      // What this tab holds now, so a change from another tab reads as a refresh or a new sign in.
      const current = this.session.get()
      if (!(current instanceof Promise)) this.#known = current
      this.session.subscribe((session) => {
        this.#changedElsewhere(session)
      })
    }
  }

  /**
   * Calls `listener` with every change to the signed in user: `signedIn`, `signedOut`,
   * `tokenRefreshed`, and `userUpdated`, including changes another tab made, and `mfaRequired`
   * when a sign in stops at the MFA step (with the factors as the third argument). Returns a
   * function that stops it.
   */
  onAuthStateChange(listener: AuthStateListener): () => void {
    this.#listeners.add(listener)
    return () => {
      this.#listeners.delete(listener)
    }
  }

  /**
   * The signed in user's session, refreshed first when under a minute of its access token is
   * left; null when nobody is signed in. Nothing refreshes on a timer; call this when your app
   * wakes up, for example.
   */
  async getSession(): Promise<AuthSession | null> {
    return this.#sessionForCall()
  }

  /**
   * Redeems a link Orvano emailed (spec 0010, AC-24): reads `orvano_type` and `orvano_token`, calls
   * the matching operation, and returns what it did, or null when the URL carries neither
   * parameter. A magic link or password reset stores the new session (replacing any) and says
   * `signedIn`; a verification or email change refreshes and says `userUpdated` when this client
   * holds that user's session. In a browser `url` defaults to `location.href`, and then the two
   * parameters are removed from the address bar after a success.
   *
   * @throws TypeError, before any call, for an unknown `orvano_type` or a `recovery` link without
   * `password`.
   * @throws {@link OrvanoError} when Orvano refuses the link, for example `invalid_email_token`.
   */
  async redeemLink(
    url?: string | URL | URLSearchParams,
    options: RedeemLinkOptions = {},
  ): Promise<LinkResult | OAuthSignInResult | IdentityLinkResult | null> {
    const location = (globalThis as { location?: { href: string } }).location
    const source = url ?? location?.href
    if (source === undefined)
      throw new TypeError('Orvano: pass the link URL; this runtime has no location.')
    const fromAddressBar = String(source) === location?.href
    const { password, ...request } = options

    // Spec 0012, AC-20: a provider redirect carries orvano_type oauth or oauth_link.
    const redirect = readOAuthRedirect(
      source instanceof URLSearchParams
        ? source
        : source instanceof URL
          ? source.searchParams
          : new URL(source, location?.href ?? 'http://localhost').searchParams,
    )
    if (redirect !== null) {
      if (redirect.error !== null) {
        clearVerifier(this)
        if (fromAddressBar) removeOAuthFromAddressBar()
        throw oauthRedirectError(redirect.error)
      }
      if (redirect.code === null || redirect.code === '')
        throw new TypeError('Orvano: the provider redirect has no orvano_code.')
      const result = await this.#oauth.redeem(redirect.type, redirect.code, this, request)
      if (fromAddressBar) removeOAuthFromAddressBar()
      return result
    }

    const link = readEmailLink(source, password)
    if (link === null) return null
    const result = await this.#emailAuth.redeemLink(link, this, request)
    if (fromAddressBar) removeLinkFromAddressBar()
    return result
  }

  /**
   * Signs in with a provider by redirect (spec 0012, AC-20): makes a PKCE verifier, keeps it in
   * `sessionStorage` (memory outside a browser), starts the flow, and opens the provider's page.
   * In a browser the page navigates away and this resolves null; call `redeemLink` on the page at
   * `redirectUrl`. An `open` that resolves with the final redirect URL gets the result at once.
   *
   * @throws TypeError, before any call, outside a browser without `open`.
   * @throws {@link OrvanoError} for a refused start (`provider_not_enabled`, ...) or redemption.
   */
  async signInWithOAuth(
    provider: OAuthProvider,
    options: OAuthOptions,
  ): Promise<OAuthSignInResult | null> {
    return (await this.#oauth.start('oauth', provider, options, this)) as OAuthSignInResult | null
  }

  /**
   * Links a provider to the signed in user by redirect, like {@link signInWithOAuth}. A user with a
   * password passes it as `password` (missing or wrong: `invalid_credentials`), unless this
   * session passed a second factor within 10 minutes (and a user with MFA on must have:
   * `mfa_verification_required`); a user without a password needs a session at most 10 minutes
   * old (`reauthentication_required`).
   */
  async linkIdentity(
    provider: OAuthProvider,
    options: LinkIdentityOptions,
  ): Promise<IdentityLinkResult | null> {
    return (await this.#oauth.start(
      'oauth_link',
      provider,
      options,
      this,
    )) as IdentityLinkResult | null
  }

  /**
   * Signs in with a provider's ID token from native Google or Apple sign in, with the raw nonce
   * from `createNonce` (spec 0012, AC-9). Stores the session and says `signedIn`.
   */
  signInWithIdToken(
    credentials: IdTokenCredentials,
    options?: RequestOptions,
  ): Promise<IdTokenSignInResult> {
    return signInWithIdToken(this, credentials, options)
  }

  /**
   * Links a provider to the signed in user with its native ID token, and says `userUpdated`. Pass
   * the user's current password as `password` when they have one, as for {@link linkIdentity}.
   */
  linkIdentityWithIdToken(
    credentials: IdTokenCredentials,
    options?: IdentityLinkOptions,
  ): Promise<Identity> {
    return linkIdentityWithIdToken(this, credentials, options)
  }

  /**
   * Signs in with a 6 digit code Orvano emailed (`account.createEmailCodeSession`), stores the
   * session, and says `signedIn`. In `@orvano/nextjs`'s browser client it goes through the app's
   * route handler, which sets the session cookies.
   *
   * @throws {@link OrvanoError} for a wrong or used up code (`invalid_code`) and every other refusal.
   */
  signInWithEmailCode(
    email: string,
    code: string,
    options?: RequestOptions,
  ): Promise<EmailCodeResult> {
    return this.#emailAuth.signInWithEmailCode(email, code, this, options)
  }

  /**
   * The sign in waiting at the MFA step (spec 0013, AC-36): its factors and expiry, or null. The
   * ticket stays in this client's memory only, so a reload or a new client starts over.
   */
  get pendingMfa(): PendingMfa | null {
    const pending = this.#mfaStore.get()
    return pending === null ? null : { factors: pending.factors, expiresAt: pending.expiresAt }
  }

  /**
   * Finishes a sign in that stopped at the MFA step (`mfaRequired`) with an authenticator app code,
   * a recovery code, or `{ passkey: true }` (`account.createMfaSession`), stores the session, and
   * says `signedIn`. A passkey runs the ceremony first: `account.createMfaPasskeyChallenge`, then
   * the passkey authenticator, then the answer.
   *
   * @throws TypeError, before any call, when no sign in is waiting for MFA.
   * @throws {@link OrvanoError} for a wrong code (`invalid_mfa_code`) or passkey
   * (`invalid_passkey`; after 5 wrong answers the ticket ends) or an ended ticket
   * (`invalid_mfa_ticket`: sign in again).
   */
  async completeMfa(
    answer: MfaAnswer | MfaWireAnswer,
    options?: RequestOptions,
  ): Promise<SignInOutcome> {
    const transport = this.#mfaTransport
    if (transport !== undefined) {
      const wire = await this.#wireAnswer(
        answer,
        () => transport.createMfaPasskeyChallenge(this, options),
        options,
      )
      return transport.completeMfa(wire, this, options)
    }
    const pending = this.#mfaStore.get()
    if (pending === null)
      throw new TypeError('Orvano: no sign in is waiting for MFA; sign in first.')
    try {
      const wire = await this.#wireAnswer(
        answer,
        () =>
          new AccountService(this).createMfaPasskeyChallenge({ ticket: pending.ticket }, options),
        options,
      )
      const result = await new AccountService(this).createMfaSession(
        { ticket: pending.ticket, ...wire },
        options,
      )
      return signInOutcome(result)
    } catch (error) {
      if (error instanceof OrvanoError && error.code === 'invalid_mfa_ticket') {
        const now = this.#mfaStore.get()
        if (now !== null && now.ticket === pending.ticket) this.#mfaStore.set(null)
      }
      throw error
    }
  }

  /**
   * Step up (spec 0013, AC-19): proves a second factor on the signed in session
   * (`account.verifyMfa`), so security changes such as `deleteTotp` work for the next 10 minutes.
   * `{ passkey: true }` runs the ceremony against `account.createStepUpPasskeyChallenge` first.
   * Stores the new access token, which carries `aal` 2, keeps the refresh token this client
   * holds (the server sends none; it is unchanged), and says `tokenRefreshed`.
   *
   * @throws {@link OrvanoError} for a wrong code (`invalid_mfa_code`) or passkey
   * (`invalid_passkey`), or a factor the user can't use now (`factor_not_enabled`).
   */
  async verifyMfa(answer: MfaAnswer | MfaWireAnswer, options?: RequestOptions): Promise<void> {
    const wire = await this.#wireAnswer(
      answer,
      () => new AccountService(this).createStepUpPasskeyChallenge(options),
      options,
    )
    if (this.#mfaTransport !== undefined) return this.#mfaTransport.verifyMfa(wire, this, options)
    const raised = await new AccountService(this).verifyMfa(wire, options)
    await this.#saveRaised(raised)
  }

  /**
   * Turns MFA on with the first code from the authenticator app (`account.confirmTotp`), after
   * `account.createTotp` (which takes the user's current password). Stores the new access token,
   * keeps the refresh token this client holds (the server sends none; it is unchanged), and says
   * `tokenRefreshed`; every other session of the user has ended. Answers the 10 recovery codes:
   * show them once.
   *
   * @throws {@link OrvanoError} for a wrong code (`invalid_mfa_code`) or no secret waiting for
   * its first code (`totp_not_pending`).
   */
  async confirmTotp(code: string, options?: RequestOptions): Promise<string[]> {
    if (this.#mfaTransport !== undefined) return this.#mfaTransport.confirmTotp(code, this, options)
    const confirmation = await new AccountService(this).confirmTotp({ code }, options)
    await this.#saveRaised(confirmation.session)
    return confirmation.recoveryCodes
  }

  /**
   * Signs in with a passkey (spec 0013, AC-23, AC-24): starts a challenge, lets the user pick a
   * passkey (a dialog, or with `autofill` the browser's autofill on a field with
   * `autocomplete="username webauthn"`), and sends its answer. Stores the session and says
   * `signedIn`. A passkey counts as two factors, so this sign in never stops at the MFA step.
   *
   * @throws {@link OrvanoError} for a refused answer (`invalid_passkey`) or passkeys turned off for
   * the project (`factor_not_enabled`); the browser's own error (for example `NotAllowedError`)
   * when the user cancels.
   */
  async signInWithPasskey(options: PasskeySignInOptions = {}): Promise<SignInOutcome> {
    const request: RequestOptions = options.signal === undefined ? {} : { signal: options.signal }
    const transport = this.#mfaTransport
    const challenge =
      transport !== undefined
        ? await transport.createPasskeyChallenge(this, request)
        : await new AccountService(this).createPasskeyChallenge(request)
    const credential = await this.#passkeys.get(challenge.options, {
      ...(options.autofill === true ? { mediation: 'conditional' as const } : {}),
      ...(options.signal === undefined ? {} : { signal: options.signal }),
    })
    const answer = { challengeId: challenge.challengeId, credential }
    if (transport !== undefined) return transport.signInWithPasskey(answer, this, request)
    return signInOutcome(await new AccountService(this).createPasskeySession(answer, request))
  }

  /**
   * Adds a passkey to the signed in user (spec 0013, AC-20, AC-21):
   * `account.createPasskeyRegistration`, the passkey authenticator, then
   * `account.completePasskeyRegistration`. A user with a password passes it as `password`, unless
   * this session passed a second factor within 10 minutes (and a user with MFA on must have); a
   * user without a password needs a session that signed in within 10 minutes. When the user has
   * an email, it must be verified.
   *
   * @throws {@link OrvanoError} for `invalid_credentials` (a missing or wrong password),
   * `reauthentication_required`, `mfa_verification_required`, `email_not_verified`,
   * `passkey_limit`, `passkey_already_registered`, or `invalid_passkey`.
   */
  async registerPasskey(options: PasskeyRegistrationOptions = {}): Promise<Passkey> {
    const request: RequestOptions = options.signal === undefined ? {} : { signal: options.signal }
    const account = new AccountService(this)
    const registration = await account.createPasskeyRegistration(
      options.password === undefined ? {} : { password: options.password },
      request,
    )
    const credential = await this.#passkeys.create(registration.options, options.signal)
    return account.completePasskeyRegistration(
      {
        challengeId: registration.challengeId,
        credential,
        ...(options.name === undefined ? {} : { name: options.name }),
      },
      request,
    )
  }

  /**
   * Whether passkeys work in this runtime (a browser with WebAuthn, or the client's own passkey
   * authenticator); with `autofill`, whether they can be offered through autofill too.
   */
  isPasskeySupported(options?: { autofill?: boolean }): Promise<boolean> {
    return this.#passkeys.isSupported(options)
  }

  /**
   * The API form of a second factor: for `{ passkey: true }`, runs the passkey ceremony; an answer
   * already in the API form (a passkey's `challengeId` and `credential`) goes as it is.
   */
  async #wireAnswer(
    answer: MfaAnswer | MfaWireAnswer,
    challenge: () => Promise<PasskeyChallenge>,
    options: RequestOptions | undefined,
  ): Promise<MfaWireAnswer> {
    if (!('passkey' in answer) || answer.passkey !== true) return answer
    const issued = await challenge()
    const credential = await this.#passkeys.get(
      issued.options,
      options?.signal === undefined ? {} : { signal: options.signal },
    )
    return { passkey: { challengeId: issued.challengeId, credential } }
  }

  /**
   * Tells listeners `mfaRequired` for a sign in another party started and holds the ticket of,
   * such as `@orvano/nextjs`'s route handler. {@link pendingMfa} then shows its factors.
   */
  announceMfa(pending: PendingMfa): void {
    this.#mfaStore.set({ ticket: '', factors: pending.factors, expiresAt: pending.expiresAt })
    this.#emit('mfaRequired', this.#known ?? null, pending)
  }

  /**
   * Reads the session store again and tells listeners `event`. For transports whose session is
   * written somewhere else, such as a route handler that sets cookies.
   */
  async reloadSession(event: AuthEvent): Promise<void> {
    const session = await this.session.get()
    this.#known = session
    this.#emit(event, session)
  }

  /**
   * Adds this client's credentials to an outgoing request: the signed in user's access token as
   * `Authorization: Bearer`, when there is one. Clients for other audiences add theirs here too.
   */
  protected authorize(headers: Headers, session: AuthSession | null): Promise<void> {
    if (session !== null) headers.set(authorizationHeader, `Bearer ${session.accessToken}`)
    return Promise.resolve()
  }

  /**
   * Sends one request and returns the parsed JSON body. Throws {@link OrvanoError} when the
   * server answers with a failure status, and the signal's reason when it is cancelled or times out.
   */
  async request<T>(spec: RequestSpec, options?: RequestOptions): Promise<T> {
    const asUser =
      spec.anonymous !== true &&
      spec.bearer === undefined &&
      spec.session !== 'start' &&
      spec.session !== 'refresh'
    const session = asUser ? await this.#sessionForCall() : null
    try {
      return await this.#send<T>(spec, options, session)
    } catch (error) {
      // Refused before it did anything, so repeating any method once is safe.
      if (session === null || !isStaleToken(error)) throw error
      const fresh = await this.#refresh(session)
      if (fresh === null) throw error
      return this.#send<T>(spec, options, fresh)
    }
  }

  async #send<T>(
    spec: RequestSpec,
    options: RequestOptions | undefined,
    session: AuthSession | null,
  ): Promise<T> {
    const url = new URL(this.#endpoint + spec.path)
    for (const [key, value] of Object.entries(spec.query ?? {})) {
      if (value !== undefined) url.searchParams.set(key, String(value))
    }
    const body = spec.body === undefined ? undefined : JSON.stringify(spec.body)
    const signal = this.#signal(options)
    const retryable = spec.method === 'GET' || spec.method === 'HEAD' || spec.idempotent === true

    for (let attempt = 0; ; attempt++) {
      const headers = new Headers(this.#headers)
      headers.set('Accept', 'application/json')
      headers.set(sdkHeader, `${sdkName}/${sdkVersion}`)
      if (this.#project !== undefined) headers.set('X-Orvano-Project', this.#project)
      if (spec.bearer === undefined) await this.authorize(headers, session)
      else headers.set(authorizationHeader, `Bearer ${spec.bearer}`)
      if (spec.noCache === true) headers.set('Cache-Control', 'no-cache')

      const init: RequestInit = { method: spec.method, headers }
      if (body !== undefined) {
        headers.set('Content-Type', 'application/json')
        init.body = body
      }
      if (signal !== undefined) init.signal = signal

      const response = await this.#fetch(url, init)
      this.#checkVersion(response)
      if (response.ok) {
        // A 204, a HEAD, or an accepted call with no body (202) answers undefined.
        const text = response.status === 204 || spec.method === 'HEAD' ? '' : await response.text()
        const result: unknown = text === '' ? undefined : JSON.parse(text)
        await this.#applySession(spec.session, result)
        return result as T
      }

      if (
        retryable &&
        attempt < this.#maxRetries &&
        (response.status === 429 || response.status === 503)
      ) {
        await response.body?.cancel()
        await sleep(retryDelayMs(response, attempt), signal)
        continue
      }
      throw await OrvanoError.fromResponse(response)
    }
  }

  /** The stored session, refreshed first when under {@link refreshMarginMs} are left. */
  async #sessionForCall(): Promise<AuthSession | null> {
    const session = await this.session.get()
    this.#known ??= session
    if (
      session === null ||
      Date.parse(session.accessTokenExpiresAt) - Date.now() >= refreshMarginMs
    )
      return session
    try {
      return await this.#refresh(session)
    } catch {
      // A network error or timeout keeps the session; the call goes ahead with the current token.
      return session
    }
  }

  /**
   * Trades `stale` for a fresh session, once per client at a time and, when the store is shared by
   * tabs, under its Web Lock. Returns null when the session is over (a 401: the store is cleared
   * and listeners hear `signedOut`); rethrows anything else, keeping the session.
   */
  #refresh(stale: AuthSession): Promise<AuthSession | null> {
    this.#refreshing ??= (async () => {
      try {
        const locks = (globalThis as { navigator?: { locks?: LockManagerLike } }).navigator?.locks
        const lockName = this.session.lockName
        return lockName !== undefined && locks !== undefined
          ? await locks.request(lockName, () => this.#refreshNow(stale))
          : await this.#refreshNow(stale)
      } finally {
        this.#refreshing = undefined
      }
    })()
    return this.#refreshing
  }

  async #refreshNow(stale: AuthSession): Promise<AuthSession | null> {
    // Another tab, or a call racing this one, may have refreshed already.
    const current = await this.session.get()
    if (current === null) return null
    if (current.accessToken !== stale.accessToken) {
      this.#known = current
      return current
    }
    try {
      const fresh = await this.#refresher(current, this)
      await this.#save(fresh, 'tokenRefreshed')
      return fresh
    } catch (error) {
      if (!(error instanceof OrvanoError) || error.status !== 401) throw error
      await this.#save(null, 'signedOut')
      return null
    }
  }

  /** Stores or clears the session after a successful sign in, refresh, or sign out. */
  async #applySession(change: RequestSpec['session'], result: unknown): Promise<void> {
    switch (change) {
      case undefined:
        return
      case 'start': {
        // Spec 0013, AC-36: a sign in that stopped at the MFA step stores nothing and keeps the
        // ticket in memory only.
        const body = result as
          | { session?: unknown; mfa?: { ticket?: unknown } | null; verificationRequired?: unknown }
          | undefined
        // Spec 0014, AC-12: a pending sign up (the project requires verified emails) has no session
        // and stores nothing; the app tells the user to check their inbox.
        if (body?.verificationRequired === true) {
          this.#mfaStore.set(null)
          return
        }
        const mfa = pendingMfaFrom(body?.mfa)
        if (mfa !== null) {
          this.#mfaStore.set(mfa)
          // The ticket lives in a cookie the caller must not read: it never leaves this client.
          if (this.#mfaStore.hidesTicket === true && body?.mfa != null) body.mfa.ticket = ''
          this.#emit('mfaRequired', this.#known ?? null, {
            factors: mfa.factors,
            expiresAt: mfa.expiresAt,
          })
          return
        }
        this.#mfaStore.set(null)
        await this.#save(sessionFrom(body?.session), 'signedIn')
        return
      }
      case 'refresh':
        await this.#save(sessionFrom(result), 'tokenRefreshed')
        return
      case 'end':
        await this.#save(null, 'signedOut')
        return
      case 'user':
        await this.#userChanged(result)
        return
    }
  }

  /**
   * A call changed a user (spec 0010, AC-14). When this client holds that user's session it says
   * `userUpdated`, refreshing first when the token's `email_verified` claim no longer matches, so
   * the next call carries the new claim. A session for another user, or none, hears nothing.
   */
  async #userChanged(result: unknown): Promise<void> {
    const session = await this.session.get()
    if (session === null) return
    const claims = readAccessClaims(session.accessToken)
    const user = result as { id?: unknown; emailVerified?: unknown } | undefined
    if (claims !== null && typeof user?.id === 'string' && claims.sub !== user.id) return
    if (
      claims !== null &&
      typeof user?.emailVerified === 'boolean' &&
      user.emailVerified !== claims.emailVerified
    ) {
      try {
        if ((await this.#refresh(session)) === null) return
      } catch {
        // A network error keeps the session; the claim updates at the next refresh.
      }
    }
    this.#emit('userUpdated', await this.session.get())
  }

  /**
   * Stores the new access token of a session raised by a second factor (spec 0013), keeping the
   * refresh token held now, and says `tokenRefreshed`.
   */
  async #saveRaised(raised: unknown): Promise<void> {
    await this.#save(raisedSessionFrom(raised, await this.session.get()), 'tokenRefreshed')
  }

  async #save(session: AuthSession | null, event: AuthEvent): Promise<void> {
    await this.session.set(session)
    this.#known = session
    this.#emit(event, session)
  }

  /** A change another tab made: tells the listeners what it amounts to here. */
  #changedElsewhere(session: AuthSession | null): void {
    const before = this.#known
    this.#known = session
    if (session === null) {
      if (before !== null) this.#emit('signedOut', null)
    } else {
      this.#emit(before?.sessionId === session.sessionId ? 'tokenRefreshed' : 'signedIn', session)
    }
  }

  #emit(event: AuthEvent, session: AuthSession | null, mfa?: PendingMfa): void {
    for (const listener of [...this.#listeners]) {
      try {
        if (mfa === undefined) listener(event, session)
        else listener(event, session, mfa)
      } catch (error) {
        this.#logger.warn(`Orvano: an onAuthStateChange listener threw: ${String(error)}`)
      }
    }
  }

  /** Warns once per client when the server's major.minor differs from this SDK's. */
  #checkVersion(response: Response): void {
    if (this.#versionChecked) return
    const version = response.headers.get(serverVersionHeader)
    if (version === null) return
    this.#versionChecked = true
    const warning = versionMismatch(version, this.#endpoint)
    if (warning !== null) this.#logger.warn(warning)
  }

  /** The call's timeout and the caller's signal, combined. */
  #signal(options: RequestOptions | undefined): AbortSignal | undefined {
    const timeoutMs = options?.timeoutMs ?? this.#timeoutMs
    const timeout = timeoutMs > 0 ? AbortSignal.timeout(timeoutMs) : undefined
    const signals = [options?.signal, timeout].filter((s) => s !== undefined)
    return signals.length > 1 ? AbortSignal.any(signals) : signals[0]
  }
}

/** The part of the Web Locks API the client uses. */
interface LockManagerLike {
  request<T>(name: string, callback: () => Promise<T>): Promise<T>
}

/** A 401 that a fresh access token may cure: `token_expired` or `invalid_token`. */
function isStaleToken(error: unknown): boolean {
  return (
    error instanceof OrvanoError &&
    error.status === 401 &&
    (error.code === 'token_expired' || error.code === 'invalid_token')
  )
}

/** `Retry-After` (seconds or an HTTP date), else exponential backoff with full jitter. */
function retryDelayMs(response: Response, attempt: number): number {
  const header = response.headers.get('Retry-After')
  if (header !== null) {
    const seconds = Number(header)
    if (Number.isFinite(seconds) && seconds >= 0) return seconds * 1000
    const date = Date.parse(header)
    if (!Number.isNaN(date)) return Math.max(0, date - Date.now())
  }
  return Math.random() * backoffBaseMs * 2 ** attempt
}

function sleep(ms: number, signal: AbortSignal | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted === true) {
      reject(signal.reason as Error)
      return
    }
    const onAbort = (): void => {
      clearTimeout(timer)
      reject(signal?.reason as Error)
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/** The ticket, factors, and expiry of an `AuthResult.mfa`, or null when the result has none. */
function pendingMfaFrom(value: unknown): PendingMfaTicket | null {
  const mfa = value as
    { ticket?: unknown; factors?: unknown; expiresAt?: unknown } | null | undefined
  if (mfa === null || mfa === undefined) return null
  if (
    typeof mfa.ticket !== 'string' ||
    typeof mfa.expiresAt !== 'string' ||
    !Array.isArray(mfa.factors)
  )
    throw new TypeError('Orvano: the MFA challenge in the response is malformed')
  return { ticket: mfa.ticket, factors: mfa.factors as MfaFactor[], expiresAt: mfa.expiresAt }
}
