// MFA and passkeys for console accounts (spec 0013, AC-41, AC-42): the twins of the account.* operations for project
// `console`. The ticket between the two sign in steps travels only in the orvano_console_mfa cookie (HttpOnly,
// SameSite=Strict, Path=/v1/console/account/session, 5 minutes), so no request here carries it. Operations that
// raise the session's strength set a new orvano_console cookie instead of returning tokens.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./mfa.tsp";
import "./passkeys.tsp";
import "./console-account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("""
  A console sign in's answer: `account` when the account is signed in, or `mfa` when it has MFA on and must pass a
  second step first. Exactly one of them is set.
  """)
model ConsoleAuthResult {
  @doc("The signed in account, with both session cookies set; null while a second step is needed.")
  account: ConsoleAccount | null;

  @doc("""
    The second step to pass with `consoleAccount.createMfaSession`; null when signed in. Its `ticket` is empty: the
    ticket travels in the `orvano_console_mfa` cookie.
    """)
  mfa: MfaChallenge | null;
}

@doc("The second step of a console sign in: exactly one factor. The ticket comes from the `orvano_console_mfa` cookie.")
model CreateConsoleMfaSessionRequest {
  @doc("The 6 digit code the authenticator app shows now.")
  @example("492039")
  totpCode?: string;

  @doc("A recovery code; case, spaces, and hyphens do not matter. Each works once.")
  @example("K7QXM-2RPLD")
  recoveryCode?: string;

  @doc("A passkey's answer to `consoleAccount.createMfaPasskeyChallenge`.")
  passkey?: PasskeyAnswer;
}

@doc("""
  MFA is on for the console account. Show the recovery codes once and ask to keep them safe; they can't be read again.
  Every other session of the account has ended, and the `orvano_console` cookie now carries level 2.
  """)
model ConsoleTotpConfirmation {
  @doc("10 recovery codes, each `XXXXX-XXXXX`, each working once.")
  @example(#["K7QXM-2RPLD", "A4ZTN-WQ6EB"])
  recoveryCodes: string[];
}

@route("/v1/console/account")
@tag("console")
interface ConsoleAccountMfaApi {
  @doc("""
    Finishes a console sign in that answered an MFA challenge: checks the `orvano_console_mfa` ticket and one factor,
    then sets both session cookies. Clears the ticket cookie on success, on an expired ticket, and after the fifth
    wrong factor.
    """)
  @operationId("consoleAccount.createMfaSession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session/mfa")
  @post
  createMfaSession(@body request: CreateConsoleMfaSessionRequest): {
    @statusCode statusCode: 201;
    @body body: ConsoleAccount;
  } | Problem;

  @doc("""
    Starts answering a console MFA challenge with a passkey: answers the options for `navigator.credentials.get`,
    listing the account's passkeys. Reads the `orvano_console_mfa` ticket. Never counts as a wrong attempt.
    """)
  @operationId("consoleAccount.createMfaPasskeyChallenge")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session/mfa/passkey-challenge")
  @post
  createMfaPasskeyChallenge(): PasskeyChallenge | Problem;

  @doc("""
    Starts a console passkey sign in: answers a challenge with an empty `allowCredentials`, so the browser offers every
    passkey for the console, including through autofill. Needs the install on https with a host name, or on localhost.
    """)
  @operationId("consoleAccount.createPasskeyChallenge")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session/passkey-challenge")
  @post
  createPasskeyChallenge(): PasskeyChallenge | Problem;

  @doc("""
    Signs a console account in with a passkey's answer to `consoleAccount.createPasskeyChallenge`, setting both
    session cookies at level 2. Never asked for MFA.
    """)
  @operationId("consoleAccount.createPasskeySession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session/passkey")
  @post
  createPasskeySession(@body request: CreatePasskeySessionRequest): {
    @statusCode statusCode: 201;
    @body body: ConsoleAccount;
  } | Problem;

  @doc("Gets the signed in console account's MFA state.")
  @operationId("consoleAccount.getMfa")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa")
  @get
  getMfa(): MfaStatus | Problem;

  @doc("""
    Starts turning on an authenticator app for the console account: answers a new secret, replacing any that still
    waits for its first code. Needs the account's current password (or a second factor on this session within 10
    minutes).
    """)
  @operationId("consoleAccount.createTotp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/totp")
  @post
  createTotp(@body request: CreateTotpRequest): {
    @statusCode statusCode: 201;
    @body body: TotpSetup;
  } | Problem;

  @doc("""
    Turns MFA on for the console account with the first code from the authenticator app. Answers 10 new recovery
    codes, ends every other session of the account, and sets a new `orvano_console` cookie at level 2.
    """)
  @operationId("consoleAccount.confirmTotp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/totp/confirm")
  @post
  confirmTotp(@body request: ConfirmTotpRequest): ConsoleTotpConfirmation | Problem;

  @doc("""
    Turns MFA off for the console account: removes the authenticator app and every recovery code. Sessions stay. Needs
    a second factor on this session within 10 minutes (`consoleAccount.verifyMfa`).
    """)
  @operationId("consoleAccount.deleteTotp")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/totp")
  @delete
  deleteTotp(): NoContentResponse | Problem;

  @doc("""
    Replaces the console account's recovery codes with 10 new ones; every older code stops working. Needs MFA on and a
    second factor on this session within 10 minutes (`consoleAccount.verifyMfa`).
    """)
  @operationId("consoleAccount.createRecoveryCodes")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/recovery-codes")
  @post
  createRecoveryCodes(): {
    @statusCode statusCode: 201;
    @body body: RecoveryCodes;
  } | Problem;

  @doc("""
    Step up for the console account: checks a second factor on this session, so security changes work for the next
    10 minutes, and sets a new `orvano_console` cookie at the new strength.
    """)
  @operationId("consoleAccount.verifyMfa")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/verify")
  @post
  verifyMfa(@body request: VerifyMfaRequest): NoContentResponse | Problem;

  @doc("""
    Starts a console step up with a passkey: answers the options for `navigator.credentials.get`, listing the account's
    passkeys. Send the passkey's answer to `consoleAccount.verifyMfa`.
    """)
  @operationId("consoleAccount.createStepUpPasskeyChallenge")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/mfa/passkey-challenge")
  @post
  createStepUpPasskeyChallenge(): PasskeyChallenge | Problem;

  @doc("""
    Starts adding a passkey to the console account: answers the options for `navigator.credentials.create`. Needs
    console passkeys (https with a host name, or localhost), the account's current password (or a second factor on
    this session within 10 minutes), and fewer than 10 passkeys.
    """)
  @operationId("consoleAccount.createPasskeyRegistration")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/passkeys/registration")
  @post
  createPasskeyRegistration(@body request: CreatePasskeyRegistrationRequest): PasskeyRegistration | Problem;

  @doc("Finishes adding a passkey to the console account with the browser's answer to `consoleAccount.createPasskeyRegistration`.")
  @operationId("consoleAccount.completePasskeyRegistration")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/passkeys")
  @post
  completePasskeyRegistration(@body request: CompletePasskeyRegistrationRequest): {
    @statusCode statusCode: 201;
    @body body: Passkey;
  } | Problem;

  @doc("Lists the console account's passkeys, oldest first, including inactive ones.")
  @operationId("consoleAccount.listPasskeys")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/passkeys")
  @get
  listPasskeys(): PasskeyList | Problem;

  @doc("Renames one of the console account's passkeys.")
  @operationId("consoleAccount.updatePasskey")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/passkeys/{passkeyId}")
  @patch(#{ implicitOptionality: false })
  updatePasskey(
    @doc("The passkey's ID.")
    @path
    passkeyId: string,

    @body request: UpdatePasskeyRequest,
  ): Passkey | Problem;

  @doc("""
    Removes one of the console account's passkeys. An account with MFA on needs a second factor on this session within
    10 minutes (`consoleAccount.verifyMfa`); one without it needs a session that signed in within 10 minutes.
    """)
  @operationId("consoleAccount.deletePasskey")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @route("/passkeys/{passkeyId}")
  @delete
  deletePasskey(
    @doc("The passkey's ID.")
    @path
    passkeyId: string,
  ): NoContentResponse | Problem;
}
