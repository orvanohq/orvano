import type { AuthResult, MfaFactor, User } from '../generated/models.js'

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

/** One second factor for `completeMfa`: an authenticator app code or a recovery code. */
export type MfaAnswer = { totpCode: string } | { recoveryCode: string }

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
