import type {
  AuthResult,
  MfaFactor,
  PasskeyAnswer,
  PasskeyChallenge,
  PasskeyRegistration,
  PasskeyRegistrationCredential,
  TotpSetup,
  User,
} from '../generated/models.js'
import type { Client, RequestOptions } from './client.js'

/**
 * A sign in that stopped at the MFA step (spec 0013, AC-36): the user has MFA on, so no session
 * exists yet. Finish it with `completeMfa` before `expiresAt`. With `enrollmentRequired` (spec
 * 0014, AC-27), the project requires MFA and the user has none yet: enroll a first factor with
 * `startTotpEnrollment` and `completeTotpEnrollment`, or `enrollPasskey`. The ticket stays inside
 * the client.
 */
export interface PendingMfa {
  /**
   * The factors the user can answer with now: `totp`, `recovery_code`, `passkey`; with
   * `enrollmentRequired`, the factors the user can enroll: `totp`, `passkey`.
   */
  factors: MfaFactor[]
  /** When the challenge stops working; after that, sign in again. */
  expiresAt: string
  /** True when the user must enroll a first factor before any session (spec 0014, AC-27). */
  enrollmentRequired: boolean
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

/**
 * One second factor for `completeMfa` and `verifyMfa`: an authenticator app code, a recovery code,
 * or `{ passkey: true }`, which runs the passkey ceremony (the challenge, then the browser's or the
 * client's `ClientConfig.passkeys`) and sends its answer.
 */
export type MfaAnswer = { totpCode: string } | { recoveryCode: string } | { passkey: true }

/**
 * A second factor as the API takes it: a passkey's `challengeId` and `credential` in place of
 * `{ passkey: true }`. `completeMfa` and `verifyMfa` take it too, for a passkey answered elsewhere.
 */
export type MfaWireAnswer =
  { totpCode: string } | { recoveryCode: string } | { passkey: PasskeyAnswer }

/**
 * Where `completeMfa`, `verifyMfa`, `confirmTotp`, and passkey sign in go when the client must not
 * hold the tokens they answer or the ticket they need (spec 0013, AC-37): `@orvano/nextjs`'s browser client posts them to the app's
 * route handler, which keeps the refresh token in its `HttpOnly` cookie. Without one, the client
 * calls Orvano itself.
 */
export interface MfaTransport {
  /** Finishes the sign in waiting at the MFA step and stores the session. */
  completeMfa(
    answer: MfaWireAnswer,
    client: Client,
    options?: RequestOptions,
  ): Promise<SignInOutcome>
  /** Step up on the signed in session, storing the new access token. */
  verifyMfa(answer: MfaWireAnswer, client: Client, options?: RequestOptions): Promise<void>
  /** Turns MFA on with the first code, storing the new access token; answers the recovery codes. */
  confirmTotp(code: string, client: Client, options?: RequestOptions): Promise<string[]>
  /** The passkey challenge for the sign in waiting at the MFA step (`account.createMfaPasskeyChallenge`). */
  createMfaPasskeyChallenge(client: Client, options?: RequestOptions): Promise<PasskeyChallenge>
  /** Starts a passkey sign in (`account.createPasskeyChallenge`). */
  createPasskeyChallenge(client: Client, options?: RequestOptions): Promise<PasskeyChallenge>
  /** Signs in with a passkey's answer (`account.createPasskeySession`) and stores the session. */
  signInWithPasskey(
    answer: PasskeyAnswer,
    client: Client,
    options?: RequestOptions,
  ): Promise<SignInOutcome>
  /** Starts enrolling an authenticator app with the waiting ticket (`account.createMfaEnrollmentTotp`). */
  startTotpEnrollment(client: Client, options?: RequestOptions): Promise<TotpSetup>
  /** Finishes it with the first code (`account.completeMfaEnrollmentTotp`) and stores the session. */
  completeTotpEnrollment(
    code: string,
    client: Client,
    options?: RequestOptions,
  ): Promise<EnrollmentOutcome>
  /** The passkey options for the waiting ticket (`account.createMfaEnrollmentPasskey`). */
  createMfaEnrollmentPasskey(client: Client, options?: RequestOptions): Promise<PasskeyRegistration>
  /** Finishes a passkey enrollment (`account.completeMfaEnrollmentPasskey`) and stores the session. */
  completeMfaEnrollmentPasskey(
    answer: PasskeyEnrollmentAnswer,
    client: Client,
    options?: RequestOptions,
  ): Promise<EnrollmentOutcome>
}

/** A new passkey for an enrollment: the registration's `challengeId`, the credential, and a name. */
export interface PasskeyEnrollmentAnswer {
  /** The `challengeId` of the `PasskeyRegistration`. */
  challengeId: string
  /** The browser's or the platform's new credential. */
  credential: PasskeyRegistrationCredential
  /** 1 to 64 characters; left out, the passkey is named `Passkey`. */
  name?: string
}

/**
 * What an enrollment did (spec 0014, AC-27): the signed in user, whose session now holds `aal` 2,
 * and after an authenticator app the 10 recovery codes to show once (null after a passkey).
 */
export interface EnrollmentOutcome {
  /** The signed in user; null when a route handler signed in for this client and did not say. */
  user: User | null
  /** 10 recovery codes after an authenticator app, each working once; null after a passkey. */
  recoveryCodes: string[] | null
}

/**
 * What a sign in helper did: the user and whether it created them, or, when `mfaRequired` is true,
 * no user yet and the factors the challenge offers. Listeners also hear `mfaRequired`.
 */
export interface SignInOutcome {
  /** The signed in user; null while `mfaRequired` is true. */
  user: User | null
  /** True when this call created the user. */
  isNewUser: boolean
  /**
   * True when the sign in stopped at the MFA step: call `completeMfa`, or with
   * `enrollmentRequired`, enroll a first factor.
   */
  mfaRequired: boolean
  /**
   * True when the project requires MFA and the user must enroll a first factor before any session
   * (spec 0014, AC-27): `startTotpEnrollment` then `completeTotpEnrollment`, or `enrollPasskey`.
   */
  enrollmentRequired: boolean
  /**
   * The factors the challenge offers, or with `enrollmentRequired` the factors the user can enroll;
   * empty when `mfaRequired` is false.
   */
  factors: MfaFactor[]
}

/** The {@link SignInOutcome} of an `AuthResult`. */
export function signInOutcome(result: AuthResult): SignInOutcome {
  // A server older than spec 0013 sends no `mfa` at all, and one older than 0014 no `enrollmentRequired`.
  const mfa = (result.mfa as AuthResult['mfa'] | undefined) ?? null
  return {
    user: result.user,
    isNewUser: result.isNewUser,
    mfaRequired: mfa !== null,
    enrollmentRequired: mfa?.enrollmentRequired === true,
    factors: mfa?.factors ?? [],
  }
}

/**
 * Whether a sign in stopped because the project requires MFA and the user has no factor yet (spec
 * 0014, AC-27): no session exists, and the user enrolls one with `startTotpEnrollment` and
 * `completeTotpEnrollment`, or `enrollPasskey`, which sign them in.
 */
export function enrollmentRequired(result: AuthResult | SignInOutcome): boolean {
  if ('enrollmentRequired' in result) return result.enrollmentRequired
  return (result.mfa as AuthResult['mfa'] | undefined)?.enrollmentRequired === true
}
