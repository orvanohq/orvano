import { AccountService } from '../generated/client.js'
import type { IdTokenProvider, Identity, OAuthProvider, User } from '../generated/models.js'
import type { Client, RequestOptions } from './client.js'
import { OrvanoError } from './error.js'

/** What a provider redirect came back for: its `orvano_type` parameter (spec 0012). */
export type OAuthLinkType = 'oauth' | 'oauth_link'

/** The query parameter Orvano sets to the handoff code after a provider redirect. */
export const codeParameter = 'orvano_code'

/** The query parameter Orvano sets to the error code when a provider redirect failed. */
export const errorParameter = 'orvano_error'

/**
 * Opens the provider's sign in page. It may resolve with the final redirect URL (for example from
 * a popup, an in app browser, or a test that follows the redirects), and the helper then redeems it
 * at once; resolving with nothing means the page navigated away and the app redeems the redirect
 * later with `redeemLink`.
 */
export type OAuthOpener = (
  url: string,
) => string | URL | undefined | Promise<string | URL | undefined>

/** Options for {@link Client.signInWithOAuth} and {@link Client.linkIdentity}. */
export interface OAuthOptions extends RequestOptions {
  /**
   * Your page or app that receives the result: a host that is one of the project's web platforms,
   * or your app's own scheme. Orvano adds `orvano_type` and `orvano_code` or `orvano_error`.
   */
  redirectUrl: string
  /** Opens the provider's page. Defaults to `location.assign` in a browser; required elsewhere. */
  open?: OAuthOpener
}

/** A finished provider sign in: the user, and whether it created them. */
export interface OAuthSignInResult {
  /** `oauth`. */
  type: 'oauth'
  /** The signed in user. */
  user: User
  /** True when the provider account had no user yet. */
  isNewUser: boolean
}

/** A finished link of a provider to the signed in user. */
export interface IdentityLinkResult {
  /** `oauth_link`. */
  type: 'oauth_link'
  /** The new identity. */
  identity: Identity
}

/** What a native sign in did: the user, and whether it created them. */
export interface IdTokenSignInResult {
  /** The signed in user. */
  user: User
  /** True when the provider account had no user yet. */
  isNewUser: boolean
}

/** A native sign in or link with a provider's ID token (spec 0012, AC-9). */
export interface IdTokenCredentials {
  /** `google` or `apple`. */
  provider: IdTokenProvider
  /** The provider's ID token. */
  idToken: string
  /** The raw nonce from {@link createNonce}; the provider got its `hashed` form. */
  nonce: string
  /** Apple only, and required for Apple: the authorization code Sign in with Apple returned. */
  authorizationCode?: string
  /** Apple only: the name Sign in with Apple returned on the first authorization. */
  name?: string | null
}

/** A nonce for native sign in: keep `raw` for Orvano, give the provider `hashed`. */
export interface Nonce {
  /** The raw value: pass it to `signInWithIdToken`. */
  raw: string
  /** Its lowercase hex SHA-256: pass it to Google or Apple sign in. */
  hashed: string
}

/**
 * How a client starts provider flows and redeems their codes. The default calls Orvano directly
 * and keeps the PKCE verifier in `sessionStorage` (memory outside a browser); `@orvano/nextjs`
 * posts to the app's route handler, which keeps it in a cookie.
 */
export interface OAuthTransport {
  /** Starts a sign in or link flow and opens the provider; null when the page navigated away. */
  start(
    purpose: OAuthLinkType,
    provider: OAuthProvider,
    options: OAuthOptions,
    client: Client,
  ): Promise<OAuthSignInResult | IdentityLinkResult | null>
  /** Redeems a provider redirect's code with the stored verifier. */
  redeem(
    type: OAuthLinkType,
    code: string,
    client: Client,
    options?: RequestOptions,
  ): Promise<OAuthSignInResult | IdentityLinkResult>
}

/** The `sessionStorage` key of a project's PKCE verifier while a flow runs. */
export function verifierStorageKey(project: string | undefined): string {
  return `orvano.oauth.${project ?? ''}`
}

const memoryVerifiers = new WeakMap<Client, string>()

/** The default {@link OAuthTransport}: the `account` operations, called from this runtime. */
export const directOAuth: OAuthTransport = {
  async start(purpose, provider, options, client) {
    const { redirectUrl, open, ...request } = options
    const opener = open ?? defaultOpener()
    if (opener === undefined)
      throw new TypeError('Orvano: pass an open function; this runtime has no location to assign.')

    const { verifier, challenge } = await createPkce()
    const account = new AccountService(client)
    const body = { provider, redirectUrl, codeChallenge: challenge }
    const flow =
      purpose === 'oauth'
        ? await account.createOAuthFlow(body, request)
        : await account.createOAuthLinkFlow(body, request)
    saveVerifier(client, verifier)
    const final = await opener(flow.url)
    if (final === undefined) return null
    return (await client.redeemLink(final, request)) as OAuthSignInResult | IdentityLinkResult
  },
  async redeem(type, code, client, options) {
    const codeVerifier = peekVerifier(client)
    if (codeVerifier === null)
      throw new TypeError(
        'Orvano: no PKCE verifier is stored for this flow; start it again with signInWithOAuth.',
      )
    const account = new AccountService(client)
    try {
      if (type === 'oauth') {
        const result = await account.createOAuthSession({ code, codeVerifier }, options)
        takeVerifier(client)
        return { type, user: result.user, isNewUser: result.isNewUser }
      }
      const identity = await account.completeOAuthLink({ code, codeVerifier }, options)
      takeVerifier(client)
      await client.reloadSession('userUpdated')
      return { type, identity }
    } catch (error) {
      // Orvano answered, so the code is spent or refused for good; a network error keeps the verifier for a retry.
      if (error instanceof OrvanoError) takeVerifier(client)
      throw error
    }
  },
}

/** The provider redirect a URL carries: its type and code, or its error; null when it is none. */
export function readOAuthRedirect(
  params: URLSearchParams,
): { type: OAuthLinkType; code: string | null; error: string | null } | null {
  const type = params.get('orvano_type')
  if (type !== 'oauth' && type !== 'oauth_link') return null
  return { type, code: params.get(codeParameter), error: params.get(errorParameter) }
}

/**
 * The error a provider redirect carried, as the typed error every SDK call throws. Its status is
 * the one the code has in Orvano's error catalog.
 */
export function oauthRedirectError(code: string): OrvanoError {
  const status = statusOf[code] ?? 400
  return new OrvanoError(status, code, `Sign in with the provider did not finish (${code}).`, null)
}

const statusOf: Record<string, number> = {
  oauth_access_denied: 403,
  provider_error: 502,
  provider_unavailable: 503,
  provider_not_enabled: 409,
  provider_not_configured: 409,
}

/** Removes `orvano_type`, `orvano_code`, and `orvano_error` from the address bar. */
export function removeOAuthFromAddressBar(): void {
  const location = (globalThis as { location?: { href: string } }).location
  const history = (
    globalThis as {
      history?: { replaceState(data: unknown, unused: string, url?: string): void; state: unknown }
    }
  ).history
  if (location === undefined || history === undefined) return
  const url = new URL(location.href)
  for (const name of ['orvano_type', codeParameter, errorParameter]) url.searchParams.delete(name)
  history.replaceState(history.state, '', url.href)
}

/** Forgets the stored verifier, after a redirect came back with an error. */
export function clearVerifier(client: Client): void {
  takeVerifier(client)
}

/**
 * A new nonce for native sign in (spec 0012, AC-20): 32 random bytes as base64url, and the
 * lowercase hex SHA-256 of that string, which Google or Apple put in the token.
 */
export async function createNonce(): Promise<Nonce> {
  const raw = base64Url(crypto.getRandomValues(new Uint8Array(32)))
  const digest = new Uint8Array(
    await crypto.subtle.digest('SHA-256', new TextEncoder().encode(raw)),
  )
  return { raw, hashed: [...digest].map((b) => b.toString(16).padStart(2, '0')).join('') }
}

/** Native ID token sign in (`account.createIdTokenSession`); stores the session. */
export async function signInWithIdToken(
  client: Client,
  credentials: IdTokenCredentials,
  options?: RequestOptions,
): Promise<IdTokenSignInResult> {
  const result = await new AccountService(client).createIdTokenSession(body(credentials), options)
  return { user: result.user, isNewUser: result.isNewUser }
}

/** Links a provider natively (`account.createIdTokenIdentity`) and says `userUpdated`. */
export async function linkIdentityWithIdToken(
  client: Client,
  credentials: IdTokenCredentials,
  options?: RequestOptions,
): Promise<Identity> {
  const identity = await new AccountService(client).createIdTokenIdentity(
    body(credentials),
    options,
  )
  await client.reloadSession('userUpdated')
  return identity
}

function body(credentials: IdTokenCredentials): {
  provider: IdTokenProvider
  idToken: string
  nonce: string
  authorizationCode?: string
  name?: string | null
} {
  return {
    provider: credentials.provider,
    idToken: credentials.idToken,
    nonce: credentials.nonce,
    ...(credentials.authorizationCode === undefined
      ? {}
      : { authorizationCode: credentials.authorizationCode }),
    ...(credentials.name === undefined ? {} : { name: credentials.name }),
  }
}

/** A new PKCE verifier (32 random bytes, base64url) and its S256 challenge. */
export async function createPkce(): Promise<{ verifier: string; challenge: string }> {
  const verifier = base64Url(crypto.getRandomValues(new Uint8Array(32)))
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))
  return { verifier, challenge: base64Url(new Uint8Array(digest)) }
}

function base64Url(bytes: Uint8Array): string {
  let binary = ''
  for (const b of bytes) binary += String.fromCharCode(b)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function defaultOpener(): OAuthOpener | undefined {
  const location = (globalThis as { location?: { assign?: (url: string) => void } }).location
  if (typeof location?.assign !== 'function') return undefined
  return (url) => {
    location.assign?.(url)
    return undefined
  }
}

function browserStorage(): Storage | undefined {
  try {
    return (globalThis as { sessionStorage?: Storage }).sessionStorage
  } catch {
    return undefined
  }
}

function saveVerifier(client: Client, verifier: string): void {
  const storage = browserStorage()
  if (storage === undefined) memoryVerifiers.set(client, verifier)
  else storage.setItem(verifierStorageKey(client.project), verifier)
}

function peekVerifier(client: Client): string | null {
  const storage = browserStorage()
  return storage === undefined
    ? (memoryVerifiers.get(client) ?? null)
    : storage.getItem(verifierStorageKey(client.project))
}

function takeVerifier(client: Client): string | null {
  const storage = browserStorage()
  if (storage === undefined) {
    const verifier = memoryVerifiers.get(client) ?? null
    memoryVerifiers.delete(client)
    return verifier
  }
  const key = verifierStorageKey(client.project)
  const verifier = storage.getItem(key)
  storage.removeItem(key)
  return verifier
}
