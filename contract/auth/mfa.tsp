// MFA (spec 0013): turning on an authenticator app, recovery codes, and the second step of sign in. Project scoped:
// every call carries X-Orvano-Project.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";

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
}

@doc("The signed in user's MFA state.")
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

  @doc("This session's tokens: a new access token carrying `aal` 2, and the current refresh token, unchanged.")
  session: SessionTokens;
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
    Needs a session that signed in within 10 minutes (or passed a second factor within 10 minutes) and, when the user
    has an email, a verified one.
    """)
  @operationId("account.createTotp")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/totp")
  @post
  createTotp(): {
    @statusCode statusCode: 201;
    @body body: TotpSetup;
  } | Problem;

  @doc("""
    Turns MFA on with the first code from the authenticator app. Answers 10 new recovery codes, ends every other
    session of the user, and raises this one to level 2 with a new access token. The SDKs keep their stored session,
    whose next refresh carries `aal` 2 too.
    """)
  @operationId("account.confirmTotp")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/mfa/totp/confirm")
  @post
  confirmTotp(@body request: ConfirmTotpRequest): TotpConfirmation | Problem;
}
