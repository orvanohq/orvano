/**
 * The Orvano SDK for apps: browsers, mobile web, and any code a user can read. It carries only
 * `client` and `both` operations and has no way to set an API key. Server code imports
 * `@orvano/js/server` instead.
 *
 * @example
 * ```ts
 * import { Client, Orvano } from '@orvano/js'
 *
 * const orvano = new Orvano(new Client({ endpoint: 'https://orvano.example.com' }))
 * const { version } = await orvano.health.get()
 * ```
 *
 * @packageDocumentation
 */
export { Client } from './runtime/client.js'
export { refreshMarginMs, refreshWithToken } from './runtime/client.js'
export type {
  ClientConfig,
  HttpMethod,
  RequestOptions,
  RequestSpec,
  SessionRefresher,
} from './runtime/client.js'
export {
  LocalStorageSessionStore,
  MemorySessionStore,
  refreshLockName,
  sessionStorageKey,
} from './runtime/auth.js'
export type { AuthEvent, AuthSession, AuthStateListener, SessionStore } from './runtime/auth.js'
export { OrvanoError, retryAfterSeconds } from './runtime/error.js'
export { MemoryPendingMfaStore, signInOutcome } from './runtime/mfa.js'
export type {
  MfaAnswer,
  MfaTransport,
  MfaWireAnswer,
  PendingMfa,
  PendingMfaStore,
  PendingMfaTicket,
  SignInOutcome,
} from './runtime/mfa.js'
export { browserPasskeys, fromBase64Url, toBase64Url } from './runtime/passkeys.js'
export type {
  PasskeyAuthenticator,
  PasskeyGetRequest,
  PasskeyRegistrationOptions,
  PasskeySignInOptions,
} from './runtime/passkeys.js'
export {
  directEmailAuth,
  emailLinkTypes,
  linkTokenParameter,
  linkTypeParameter,
  readEmailLink,
  removeLinkFromAddressBar,
} from './runtime/links.js'
export type {
  EmailAuthTransport,
  EmailCodeResult,
  EmailLink,
  EmailLinkType,
  LinkResult,
  RedeemLinkOptions,
} from './runtime/links.js'
export {
  codeParameter,
  createNonce,
  createPkce,
  directOAuth,
  errorParameter,
  oauthRedirectError,
  readOAuthRedirect,
  removeOAuthFromAddressBar,
  verifierStorageKey,
} from './runtime/oauth.js'
export type {
  IdTokenCredentials,
  IdTokenSignInResult,
  IdentityLinkOptions,
  IdentityLinkResult,
  LinkIdentityOptions,
  Nonce,
  OAuthLinkType,
  OAuthOpener,
  OAuthOptions,
  OAuthSignInResult,
  OAuthTransport,
} from './runtime/oauth.js'
export { paginate } from './runtime/pagination.js'
export type { Page } from './runtime/pagination.js'
export { decodeEvent } from './runtime/events.js'
export type { EventRegistry } from './runtime/events.js'
export { eventRegistry } from './generated/events.js'
export { ErrorCode } from './generated/errors.js'
export * from './generated/client.js'
export type * from './generated/models.js'
export { sdkVersion } from './generated/version.js'
export type { Logger } from './runtime/version.js'
