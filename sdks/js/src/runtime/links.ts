import { AccountService } from '../generated/client.js'
import type { Client, RequestOptions } from './client.js'
import { signInOutcome } from './mfa.js'
import type { SignInOutcome } from './mfa.js'

/**
 * What an emailed link is for: its `orvano_type` parameter (spec 0010). The link also carries the
 * secret as `orvano_token`.
 */
export type EmailLinkType = 'verification' | 'recovery' | 'magic_link' | 'email_change'

/** Every {@link EmailLinkType}, in the order the contract lists them. */
export const emailLinkTypes: readonly EmailLinkType[] = [
  'verification',
  'recovery',
  'magic_link',
  'email_change',
]

/** The query parameter Orvano sets to the link's {@link EmailLinkType}. */
export const linkTypeParameter = 'orvano_type'

/** The query parameter Orvano sets to the link's single use token. */
export const linkTokenParameter = 'orvano_token'

/** An emailed link, read from its URL: what it is for, its token, and a new password for a reset. */
export interface EmailLink {
  /** What the link is for. */
  type: EmailLinkType
  /** The `orvano_token` parameter; it works once. */
  token: string
  /** The new password, for a `recovery` link only. */
  password?: string
}

/**
 * What a redeemed link did: its type, the user, and whether the call created them. A magic link or
 * reset for a user with MFA on stops at the MFA step: `mfaRequired` is true and `user` null.
 */
export interface LinkResult extends SignInOutcome {
  /** What the link was for. */
  type: EmailLinkType
}

/**
 * What an email code sign in did: the user, and whether the call created them, or the MFA step it
 * stopped at (`mfaRequired`).
 */
export type EmailCodeResult = SignInOutcome

/** Options for {@link Client.redeemLink}. */
export interface RedeemLinkOptions extends RequestOptions {
  /** The new password: required for a `recovery` link, ignored otherwise. */
  password?: string
}

/**
 * How a client redeems emailed links and codes. The default calls Orvano directly;
 * `@orvano/nextjs` posts them to the app's route handler, which sets the session cookies.
 */
export interface EmailAuthTransport {
  /** Redeems one link, storing or refreshing the session as its type needs. */
  redeemLink(link: EmailLink, client: Client, options?: RequestOptions): Promise<LinkResult>
  /** Signs in with an emailed code and stores the session. */
  signInWithEmailCode(
    email: string,
    code: string,
    client: Client,
    options?: RequestOptions,
  ): Promise<EmailCodeResult>
}

/**
 * The default {@link EmailAuthTransport}: the matching `account` operation. Session links store
 * the session and say `signedIn`; the others tell listeners `userUpdated` (after a refresh when the
 * token's `email_verified` claim is out of date) when the client holds that user's session.
 */
export const directEmailAuth: EmailAuthTransport = {
  async redeemLink(link, client, options) {
    const account = new AccountService(client)
    switch (link.type) {
      case 'magic_link': {
        const result = await account.createMagicLinkSession({ token: link.token }, options)
        return { type: link.type, ...signInOutcome(result) }
      }
      case 'recovery': {
        const result = await account.completeRecovery(
          { token: link.token, password: link.password ?? '' },
          options,
        )
        return { type: link.type, ...signInOutcome(result) }
      }
      case 'verification':
        return {
          type: link.type,
          user: await account.verifyEmail({ token: link.token }, options),
          isNewUser: false,
          mfaRequired: false,
          factors: [],
        }
      case 'email_change':
        return {
          type: link.type,
          user: await account.confirmEmailChange({ token: link.token }, options),
          isNewUser: false,
          mfaRequired: false,
          factors: [],
        }
    }
  },
  async signInWithEmailCode(email, code, client, options) {
    const result = await new AccountService(client).createEmailCodeSession({ email, code }, options)
    return signInOutcome(result)
  },
}

/**
 * Reads an emailed link from a URL (or just its query): null when it carries neither
 * `orvano_type` nor `orvano_token`. Throws a `TypeError`, before any call, for an unknown type, a
 * missing token, or a `recovery` link without a password.
 */
export function readEmailLink(
  url: string | URL | URLSearchParams,
  password?: string,
): EmailLink | null {
  const params = searchParams(url)
  const type = params.get(linkTypeParameter)
  const token = params.get(linkTokenParameter)
  if (type === null && token === null) return null
  if (!isLinkType(type))
    throw new TypeError(`Orvano: unknown ${linkTypeParameter} "${type ?? ''}".`)
  if (token === null || token === '')
    throw new TypeError(`Orvano: the link has no ${linkTokenParameter}.`)
  if (type === 'recovery') {
    if (password === undefined || password === '')
      throw new TypeError('Orvano: a recovery link needs the new password.')
    return { type, token, password }
  }
  return { type, token }
}

/** Removes `orvano_type` and `orvano_token` from the address bar, keeping everything else. */
export function removeLinkFromAddressBar(): void {
  const location = (globalThis as { location?: { href: string } }).location
  const history = (
    globalThis as {
      history?: { replaceState(data: unknown, unused: string, url?: string): void; state: unknown }
    }
  ).history
  if (location === undefined || history === undefined) return
  const url = new URL(location.href)
  url.searchParams.delete(linkTypeParameter)
  url.searchParams.delete(linkTokenParameter)
  history.replaceState(history.state, '', url.href)
}

function searchParams(url: string | URL | URLSearchParams): URLSearchParams {
  if (url instanceof URLSearchParams) return url
  if (url instanceof URL) return url.searchParams
  const base = (globalThis as { location?: { href: string } }).location?.href ?? 'http://localhost'
  return new URL(url, base).searchParams
}

function isLinkType(value: string | null): value is EmailLinkType {
  return value !== null && (emailLinkTypes as readonly string[]).includes(value)
}
