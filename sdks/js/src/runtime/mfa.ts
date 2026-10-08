import type { AuthResult, MfaFactor, User } from '../generated/models.js'
import type { Client, RequestOptions } from './client.js'

/**
 * A sign in that stopped at the MFA step (spec 0013, AC-36): the user has MFA on, so no session
 * exists yet. Finish it with `completeMfa` before `expiresAt`. The ticket stays inside the client.
 */
export interface PendingMfa {
  /** The factors the user can answer with now: `totp`, `recovery_code`, `passkey`. */
  factors: MfaFactor[]
  /** When the challenge stops working; after that, sign in again. */
  expiresAt: string
}

/** A {@link PendingMfa} with its ticket, as a {@link PendingMfaStore} keeps it. Never log it. */
export interface PendingMfaTicket extends PendingMfa {
  /** The `orv_mt_` ticket, or empty when it lives somewhere this client can't read. */
  ticket: string
}

/**
 * Where a sign in waiting at the MFA step keeps its ticket (spec 0013, AC-36, AC-37). The default
 * keeps it in memory only, never in the session store, so a reload starts over. `@orvano/nextjs`
 * keeps it in an `HttpOnly` cookie on the server.
 */
export interface PendingMfaStore {
  /** The waiting sign in, or null. */
  get(): PendingMfaTicket | null
  /** Keeps a waiting sign in, or forgets it (null). */
  set(pending: PendingMfaTicket | null): void
  /**
   * True when the ticket must not reach the code that called the sign in (an `HttpOnly` cookie
   * holds it): the client then blanks `AuthResult.mfa.ticket` before returning the result.
   */
  readonly hidesTicket?: boolean
}

/** The default {@link PendingMfaStore}: this client's memory. */
export class MemoryPendingMfaStore implements PendingMfaStore {
  #pending: PendingMfaTicket | null = null

  get(): PendingMfaTicket | null {
    return this.#pending
  }

  set(pending: PendingMfaTicket | null): void {
    this.#pending = pending
  }
}

/** One second factor for `completeMfa` and `verifyMfa`: an authenticator app code or a recovery code. */
export type MfaAnswer = { totpCode: string } | { recoveryCode: string }

/**
 * Where `completeMfa`, `verifyMfa`, and `confirmTotp` go when the client must not hold the
 * tokens they answer (spec 0013, AC-37): `@orvano/nextjs`'s browser client posts them to the app's
 * route handler, which keeps the refresh token in its `HttpOnly` cookie. Without one, the client
 * calls Orvano itself.
 */
export interface MfaTransport {
  /** Finishes the sign in waiting at the MFA step and stores the session. */
  completeMfa(answer: MfaAnswer, client: Client, options?: RequestOptions): Promise<SignInOutcome>
  /** Step up on the signed in session, storing the new access token. */
  verifyMfa(answer: MfaAnswer, client: Client, options?: RequestOptions): Promise<void>
  /** Turns MFA on with the first code, storing the new access token; answers the recovery codes. */
  confirmTotp(code: string, client: Client, options?: RequestOptions): Promise<string[]>
}

/**
 * What a sign in helper did: the user and whether it created them, or, when `mfaRequired` is true,
 * no user yet and the factors the challenge offers. Listeners also hear `mfaRequired`.
 */
export interface SignInOutcome {
  /** The signed in user; null while `mfaRequired` is true. */
  user: User | null
  /** True when this call created the user. A new user is never challenged. */
  isNewUser: boolean
  /** True when the sign in stopped at the MFA step: call `completeMfa`. */
  mfaRequired: boolean
  /** The factors the challenge offers; empty when `mfaRequired` is false. */
  factors: MfaFactor[]
}

/** The {@link SignInOutcome} of an `AuthResult`. */
export function signInOutcome(result: AuthResult): SignInOutcome {
  // A server older than spec 0013 sends no `mfa` at all.
  const mfa = (result.mfa as AuthResult['mfa'] | undefined) ?? null
  return {
    user: result.user,
    isNewUser: result.isNewUser,
    mfaRequired: mfa !== null,
    factors: mfa?.factors ?? [],
  }
}
