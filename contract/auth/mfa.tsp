// MFA (spec 0013): turning on an authenticator app, recovery codes, and the second step of sign in. Project scoped:
// every call carries X-Orvano-Project.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";
import "./passkeys.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("A second factor.")
enum MfaFactor {
  @doc("A 6 digit code from an authenticator app (TOTP).")
  totp,

  @doc("One of the user's recovery codes, for when the phone is gone.")
  recovery_code,

  @doc("A passkey.")
  passkey,
}

@doc("""
  The second step a sign in must pass before it gets a session: the user has MFA on. Answer it with
  `account.createMfaSession` before `expiresAt`.
  """)
model MfaChallenge {
  @doc("""
    Proves the first step passed; send it to `account.createMfaSession`. Keep it in memory only. Empty when the ticket
    travels in a cookie instead (the console).
    """)
  @example("orv_mt_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  ticket: string;

  @doc("The factors the user can answer with now, in this order: `totp`, `recovery_code`, `passkey`.")
  factors: MfaFactor[];

  @doc("When the ticket stops working: 5 minutes after the first step. After that, sign in again.")
  expiresAt: utcDateTime;
}

@doc("The second step of a sign in: the ticket and exactly one factor.")
model CreateMfaSessionRequest {
  @doc("The `ticket` of the `MfaChallenge`.")
  @example("orv_mt_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  ticket: string;

  @doc("The 6 digit code the authenticator app shows now.")
  @example("492039")
  totpCode?: string;

  @doc("A recovery code; case, spaces, and hyphens do not matter. Each works once.")
  @example("K7QXM-2RPLD")
  recoveryCode?: string;

  @doc("A passkey's answer to `account.createMfaPasskeyChallenge`.")
  passkey?: PasskeyAnswer;
}

@doc("The ticket of a sign in waiting for its second step.")
model CreateMfaPasskeyChallengeRequest {
  @doc("The `ticket` of the `MfaChallenge`.")
  @example("orv_mt_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  ticket: string;
}

@doc("A user's MFA state.")
model MfaStatus {
  @doc("Whether sign in asks for a second factor: a confirmed authenticator app, while the project allows TOTP.")
  @example(true)
  mfaEnabled: boolean;

  @doc("Whether an authenticator app is confirmed, even while the project has TOTP turned off.")
  @example(true)
  totpConfirmed: boolean;

  @doc("When the authenticator app was confirmed; null when none is.")
  totpConfirmedAt: utcDateTime | null;

  @doc("How many unused recovery codes are left, 0 to 10.")
  @example(10)
  recoveryCodesRemaining: int32;

  @doc("How many of the user's passkeys can sign in now.")
  @example(0)
  passkeyCount: int32;

  @doc("What the project lets users turn on now: `totp` and `passkey`.")
  factorsAvailable: MfaFactor[];
}

@doc("""
  A new authenticator app secret, waiting for its first code. Show `uri` as a QR code and `secret` for typing in, then
  confirm with `account.confirmTotp` within 15 minutes.
  """)
model TotpSetup {
  @doc("The secret: 20 random bytes as unpadded base32 (32 characters). Never log it.")
  @example("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP")
  secret: string;

  @doc("The `otpauth://` URI an authenticator app scans: the project as the issuer, the user's email as the label.")
  @example("otpauth://totp/Acme:ada%40example.com?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=Acme&algorithm=SHA1&digits=6&period=30")
  uri: string;

  @doc("When the secret stops waiting for its first code; ask for a new one after that.")
  expiresAt: utcDateTime;
}

@doc("A request to start turning on an authenticator app.")
model CreateTotpRequest {
  @doc("""
    The user's current password. A user who has one sends it, unless this session passed a second factor within 10
    minutes. A user without one leaves it out, and must have signed in within 10 minutes.
    """)
  @example("correct horse battery staple")
  password?: string;
}

@doc("The first code from the authenticator app, which turns MFA on.")
model ConfirmTotpRequest {
  @doc("The 6 digit code the authenticator app shows now.")
  @example("492039")
  code: string;
}

@doc("""
  MFA is on. Show the recovery codes once and ask the user to keep them safe; they can't be read again. Every other
  session of the user has ended, and this one is now at level 2.
  """)
model TotpConfirmation {
  @doc("10 recovery codes, each `XXXXX-XXXXX`, each working once.")
  @example(#["K7QXM-2RPLD", "A4ZTN-WQ6EB"])
  recoveryCodes: string[];

  @doc("A new access token for this session, carrying `aal` 2. Keep the refresh token you hold; it is unchanged.")
  session: RaisedSession;
}

@doc("""
  The signed in session after a second factor: a new access token carrying the new `aal` and `amr` claims. The refresh
  token is not sent; keep the one you hold, which is unchanged and whose next refresh carries the new claims too.
  """)
model RaisedSession {
  @doc("An ES256 JWT, valid for 15 minutes.")
  @example("eyJhbGciOiJFUzI1NiIsImtpZCI6Ii4uLiJ9.e30.c2ln")
  accessToken: string;

  @doc("When the access token expires.")
  accessTokenExpiresAt: utcDateTime;

  @doc("The session ID, also the `sid` claim of the access token.")
  @example("01924f5e-9a1b-7c4d-8e21-3d5e6f7a8b9c")
  sessionId: string;
}

@doc("New recovery codes. Show them once and ask the user to keep them safe; every older code stopped working.")
model RecoveryCodes {
  @doc("10 recovery codes, each `XXXXX-XXXXX`, each working once.")
  @example(#["K7QXM-2RPLD", "A4ZTN-WQ6EB"])
  codes: string[];
}

@doc("A second factor for a step up: exactly one of the fields.")
model VerifyMfaRequest {
  @doc("The 6 digit code the authenticator app shows now.")
  @example("492039")
  totpCode?: string;

  @doc("A recovery code; case, spaces, and hyphens do not matter. Each works once.")
  @example("K7QXM-2RPLD")
  recoveryCode?: string;

  @doc("A passkey's answer to `account.createStepUpPasskeyChallenge`.")
  passkey?: PasskeyAnswer;
}

@route("/v1/account")
@tag("account")
interface AccountMfaApi {
  @doc("""
    Finishes a sign in that answered an MFA challenge: checks the ticket and one factor, then creates the session.
    After 5 wrong factors the ticket stops working; sign in again.
    """)
  @operationId("account.createMfaSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/mfa")
  @post
  createMfaSession(@body request: CreateMfaSessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Starts answering an MFA challenge with a passkey: answers the options for `navigator.credentials.get`, listing the
    user's passkeys. Send the passkey's answer to `account.createMfaSession`. Never counts as a wrong attempt.
    """)
  @operationId("account.createMfaPasskeyChallenge")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @route("/sessions/mfa/passkey-challenge")
  @post
  createMfaPasskeyChallenge(@body request: CreateMfaPasskeyChallengeRequest): PasskeyChallenge | Problem;

  @doc("Gets the signed in user's MFA state.")
  @operationId("account.getMfa")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa")
  @get
  getMfa(): MfaStatus | Problem;

  @doc("""
    Starts turning on an authenticator app: answers a new secret, replacing any that still waits for its first code.
    Needs the user's current password when they have one (or a second factor on this session within 10 minutes; a
    user without a password needs a session that signed in within 10 minutes) and, when the user has an email, a
    verified one.
    """)
  @operationId("account.createTotp")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/totp")
  @post
  createTotp(@body request: CreateTotpRequest): {
    @statusCode statusCode: 201;
    @body body: TotpSetup;
  } | Problem;

  @doc("""
    Turns MFA on with the first code from the authenticator app, within 15 minutes of `account.createTotp`. Answers 10
    new recovery codes, ends every other session of the user, and raises this one to level 2 with a new access token.
    The SDKs keep their stored refresh token, whose next refresh carries `aal` 2 too.
    """)
  @operationId("account.confirmTotp")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/totp/confirm")
  @post
  confirmTotp(@body request: ConfirmTotpRequest): TotpConfirmation | Problem;

  @doc("""
    Turns MFA off: removes the authenticator app and every recovery code. Sessions stay. Needs a second factor on this
    session within 10 minutes (`account.verifyMfa`).
    """)
  @operationId("account.deleteTotp")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/totp")
  @delete
  deleteTotp(): NoContentResponse | Problem;

  @doc("""
    Replaces the user's recovery codes with 10 new ones; every older code stops working. Needs MFA on and a second
    factor on this session within 10 minutes (`account.verifyMfa`).
    """)
  @operationId("account.createRecoveryCodes")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/recovery-codes")
  @post
  createRecoveryCodes(): {
    @statusCode statusCode: 201;
    @body body: RecoveryCodes;
  } | Problem;

  @doc("""
    Step up: checks a second factor on the signed in session, so security changes work for the next 10 minutes. Raises
    the session to level 2 and answers a new access token; the refresh token is not sent and stays unchanged. The SDK
    helpers (`verifyMfa`) store the access token.
    """)
  @operationId("account.verifyMfa")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/verify")
  @post
  verifyMfa(@body request: VerifyMfaRequest): RaisedSession | Problem;

  @doc("""
    Starts a step up with a passkey: answers the options for `navigator.credentials.get`, listing the signed in user's
    passkeys. Send the passkey's answer to `account.verifyMfa`.
    """)
  @operationId("account.createStepUpPasskeyChallenge")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/passkey-challenge")
  @post
  createStepUpPasskeyChallenge(): PasskeyChallenge | Problem;
}
