// Passkeys (spec 0013): registration, sign in with no typing, list, rename, and delete, plus the WebAuthn Level 3 JSON
// forms the browser's `PublicKeyCredential` helpers read and write. Binary values are base64url strings without
// padding. Project scoped: every call carries X-Orvano-Project.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("The relying party of a passkey: the app's passkey domain and the name people see.")
model PasskeyRelyingParty {
  @doc("The RP ID: the domain passkeys are bound to.")
  @example("example.com")
  id: string;

  @doc("The name an authenticator shows: the project's RP name, else the project name.")
  @example("Acme")
  name: string;
}

@doc("The account a new passkey is for, as the authenticator stores it.")
model PasskeyUserEntity {
  @doc("The user handle: the user ID's 16 bytes, base64url.")
  @example("AZK1JbJ8dXqZ6m8RkF6uQg")
  id: string;

  @doc("The user's email, else their user ID.")
  @example("ada@example.com")
  name: string;

  @doc("The user's name, else their email, else `User`.")
  @example("Ada Lovelace")
  displayName: string;
}

@doc("A key type the server accepts.")
model PasskeyCredentialParameters {
  @doc("Always `public-key`.")
  @example("public-key")
  type: string;

  @doc("A COSE algorithm: -7 (ES256), -8 (EdDSA), or -257 (RS256).")
  @example(-7)
  alg: int32;
}

@doc("One passkey, named by its credential ID.")
model PasskeyCredentialDescriptor {
  @doc("Always `public-key`.")
  @example("public-key")
  type: string;

  @doc("The credential ID, base64url.")
  @example("pM2Gq8pZ1wqTNO4FqVt9fA")
  id: string;

  @doc("How the authenticator was reached when the passkey was made: `internal`, `hybrid`, `usb`, `nfc`, or `ble`.")
  @example(#["internal", "hybrid"])
  transports?: string[];
}

@doc("What kind of authenticator a new passkey must come from.")
model PasskeyAuthenticatorSelection {
  @doc("Always `required`: the passkey is discoverable, so it signs in with no email typed.")
  @example("required")
  residentKey: string;

  @doc("Always true, for older browsers.")
  @example(true)
  requireResidentKey: boolean;

  @doc("Always `required`: face, fingerprint, or PIN.")
  @example("required")
  userVerification: string;
}

@doc("""
  The options for `navigator.credentials.create` in WebAuthn's JSON form. Pass them to
  `PublicKeyCredential.parseCreationOptionsFromJSON`, or let the SDK's `registerPasskey` do it.
  """)
model PasskeyCreationOptions {
  @doc("The relying party.")
  rp: PasskeyRelyingParty;

  @doc("The user the passkey is for.")
  user: PasskeyUserEntity;

  @doc("32 random bytes, base64url, used once.")
  @example("Q2hhbGxlbmdlQ2hhbGxlbmdlQ2hhbGxlbmdlQ2hhbGw")
  challenge: string;

  @doc("The key types the server accepts, in order of preference.")
  pubKeyCredParams: PasskeyCredentialParameters[];

  @doc("How long the browser waits, in milliseconds: 300000.")
  @example(300000)
  timeout: int32;

  @doc("The user's passkeys under this RP ID, so the same authenticator is not registered twice.")
  excludeCredentials: PasskeyCredentialDescriptor[];

  @doc("The authenticator rules.")
  authenticatorSelection: PasskeyAuthenticatorSelection;

  @doc("Always `none`: no device certificate is asked for.")
  @example("none")
  attestation: string;
}

@doc("""
  The options for `navigator.credentials.get` in WebAuthn's JSON form. Pass them to
  `PublicKeyCredential.parseRequestOptionsFromJSON`, or let the SDK's helpers do it.
  """)
model PasskeyRequestOptions {
  @doc("32 random bytes, base64url, used once.")
  @example("Q2hhbGxlbmdlQ2hhbGxlbmdlQ2hhbGxlbmdlQ2hhbGw")
  challenge: string;

  @doc("The RP ID.")
  @example("example.com")
  rpId: string;

  @doc("How long the browser waits, in milliseconds: 300000.")
  @example(300000)
  timeout: int32;

  @doc("Always `required`: face, fingerprint, or PIN.")
  @example("required")
  userVerification: string;

  @doc("The passkeys that may answer; empty for sign in, so the browser offers every passkey for the RP ID.")
  allowCredentials: PasskeyCredentialDescriptor[];
}

@doc("The authenticator's answer to `navigator.credentials.create`.")
model PasskeyAttestationResponse {
  @doc("The client data, base64url.")
  clientDataJSON: string;

  @doc("The attestation object, base64url.")
  attestationObject: string;

  @doc("How the authenticator was reached: `internal`, `hybrid`, `usb`, `nfc`, or `ble`.")
  @example(#["internal"])
  transports?: string[];
}

@doc("A new passkey in WebAuthn's JSON form: what `PublicKeyCredential.toJSON()` gives after `create`.")
model PasskeyRegistrationCredential {
  @doc("The credential ID, base64url.")
  @example("pM2Gq8pZ1wqTNO4FqVt9fA")
  id: string;

  @doc("The credential ID again, base64url; must equal `id`.")
  @example("pM2Gq8pZ1wqTNO4FqVt9fA")
  rawId: string;

  @doc("Always `public-key`.")
  @example("public-key")
  type: string;

  @doc("The authenticator's answer.")
  response: PasskeyAttestationResponse;

  @doc("`platform` or `cross-platform`; null when the browser does not say.")
  @example("platform")
  authenticatorAttachment?: string | null;
}

@doc("The authenticator's answer to `navigator.credentials.get`.")
model PasskeyAssertionResponse {
  @doc("The client data, base64url.")
  clientDataJSON: string;

  @doc("The authenticator data, base64url.")
  authenticatorData: string;

  @doc("The signature, base64url.")
  signature: string;

  @doc("The user handle the passkey stores, base64url; null when the authenticator gives none.")
  userHandle?: string | null;
}

@doc("A passkey's answer in WebAuthn's JSON form: what `PublicKeyCredential.toJSON()` gives after `get`.")
model PasskeyAssertionCredential {
  @doc("The credential ID, base64url.")
  @example("pM2Gq8pZ1wqTNO4FqVt9fA")
  id: string;

  @doc("The credential ID again, base64url; must equal `id`.")
  @example("pM2Gq8pZ1wqTNO4FqVt9fA")
  rawId: string;

  @doc("Always `public-key`.")
  @example("public-key")
  type: string;

  @doc("The authenticator's answer.")
  response: PasskeyAssertionResponse;

  @doc("`platform` or `cross-platform`; null when the browser does not say.")
  @example("platform")
  authenticatorAttachment?: string | null;
}

@doc("A challenge for a passkey to sign: pass `options` to the browser, then send its answer with `challengeId`.")
model PasskeyChallenge {
  @doc("Names this challenge; send it back with the answer within 5 minutes.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  challengeId: string;

  @doc("The options for `navigator.credentials.get`.")
  options: PasskeyRequestOptions;
}

@doc("A started passkey registration: pass `options` to the browser, then send its answer with `challengeId`.")
model PasskeyRegistration {
  @doc("Names this registration; send it back with the new passkey within 5 minutes.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  challengeId: string;

  @doc("The options for `navigator.credentials.create`.")
  options: PasskeyCreationOptions;
}

@doc("A passkey's answer to a challenge.")
model PasskeyAnswer {
  @doc("The `challengeId` of the `PasskeyChallenge`.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  challengeId: string;

  @doc("The browser's or the platform's answer.")
  credential: PasskeyAssertionCredential;
}

@doc("A passkey of a user.")
model Passkey {
  @doc("The passkey's ID.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  id: string;

  @doc("The name the user gave it, else `Passkey`.")
  @example("MacBook")
  name: string;

  @doc("When it was added.")
  createdAt: utcDateTime;

  @doc("When it last signed in or answered a challenge; null when it never has.")
  lastUsedAt: utcDateTime | null;

  @doc("Whether the passkey is backed up and synced across devices (for example by iCloud Keychain or a password manager).")
  @example(true)
  synced: boolean;

  @doc("Whether it can sign in now: false after the project's RP ID changed away from the one it was made for.")
  @example(true)
  active: boolean;
}

@doc("A user's passkeys, oldest first; at most 10.")
model PasskeyList {
  @doc("The passkeys.")
  items: Passkey[];
}

@doc("A request to start adding a passkey.")
model CreatePasskeyRegistrationRequest {
  @doc("""
    The user's current password. A user who has one sends it, unless this session passed a second factor within 10
    minutes. A user without one leaves it out, and must have signed in within 10 minutes.
    """)
  @example("correct horse battery staple")
  password?: string;
}

@doc("A finished passkey registration.")
model CompletePasskeyRegistrationRequest {
  @doc("The `challengeId` of the `PasskeyRegistration`.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  challengeId: string;

  @doc("The browser's or the platform's answer.")
  credential: PasskeyRegistrationCredential;

  @doc("1 to 64 characters. Left out, the passkey is named `Passkey`.")
  @example("MacBook")
  name?: string;
}

@doc("A new name for a passkey.")
model UpdatePasskeyRequest {
  @doc("1 to 64 characters.")
  @example("Work laptop")
  name: string;
}

@doc("A sign in with a passkey.")
model CreatePasskeySessionRequest {
  @doc("The `challengeId` of the `PasskeyChallenge`.")
  @example("01928f3e-6c1a-7b2d-9e4f-5a6b7c8d9e0f")
  challengeId: string;

  @doc("The browser's or the platform's answer.")
  credential: PasskeyAssertionCredential;
}

@route("/v1/account")
@tag("account")
interface AccountPasskeysApi {
  @doc("""
    Starts a passkey sign in: answers a challenge with an empty `allowCredentials`, so the browser or the platform
    offers every passkey of the RP ID, including through autofill. Needs passkeys turned on for the project.
    """)
  @operationId("account.createPasskeyChallenge")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @route("/sessions/passkey-challenge")
  @post
  createPasskeyChallenge(): PasskeyChallenge | Problem;

  @doc("""
    Signs in with a passkey's answer to `account.createPasskeyChallenge`. A passkey counts as two factors, so this sign
    in is never asked for MFA and the session starts at level 2.
    """)
  @operationId("account.createPasskeySession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/passkey")
  @post
  createPasskeySession(@body request: CreatePasskeySessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Starts adding a passkey: answers the options for `navigator.credentials.create`. Needs passkeys turned on, the
    user's current password when they have one (or a second factor on this session within 10 minutes; a user without a
    password needs a session that signed in within 10 minutes), a verified email when the user has one, and fewer than
    10 passkeys.
    """)
  @operationId("account.createPasskeyRegistration")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/passkeys/registration")
  @post
  createPasskeyRegistration(@body request: CreatePasskeyRegistrationRequest): PasskeyRegistration | Problem;

  @doc("Finishes adding a passkey with the browser's or the platform's answer to `account.createPasskeyRegistration`.")
  @operationId("account.completePasskeyRegistration")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/passkeys")
  @post
  completePasskeyRegistration(@body request: CompletePasskeyRegistrationRequest): {
    @statusCode statusCode: 201;
    @body body: Passkey;
  } | Problem;

  @doc("Lists the signed in user's passkeys, oldest first, including inactive ones.")
  @operationId("account.listPasskeys")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/passkeys")
  @get
  listPasskeys(): PasskeyList | Problem;

  @doc("Renames one of the signed in user's passkeys.")
  @operationId("account.updatePasskey")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/passkeys/{passkeyId}")
  @patch(#{ implicitOptionality: false })
  updatePasskey(
    @doc("The passkey's ID.")
    @path
    passkeyId: string,

    @body request: UpdatePasskeyRequest,
  ): Passkey | Problem;

  @doc("""
    Removes one of the signed in user's passkeys. A user with MFA on needs a second factor on this session within 10
    minutes (`account.verifyMfa`); a user without it needs a session that signed in within 10 minutes.
    """)
  @operationId("account.deletePasskey")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/passkeys/{passkeyId}")
  @delete
  deletePasskey(
    @doc("The passkey's ID.")
    @path
    passkeyId: string,
  ): NoContentResponse | Problem;
}

@doc("""
  A project's second factor and passkey settings, with what the Passkeys card shows beside them. A project that never
  saved any reads as the defaults: TOTP on, passkeys off.
  """)
model AuthMethodSettings {
  @doc("Whether users can turn on an authenticator app. Turned off, nobody is asked for MFA; stored factors stay.")
  @example(true)
  totpEnabled: boolean;

  @doc("Whether users can add passkeys and sign in with them. Needs `rpId`.")
  @example(true)
  passkeysEnabled: boolean;

  @doc("The passkey domain: a lowercase host name such as `example.com` (no scheme, port, path, or IP address), or `localhost`.")
  @example("example.com")
  rpId: string | null;

  @doc("The name authenticators show, 1 to 64 characters; null means the project name.")
  @example("Acme")
  rpName: string | null;

  @doc("SHA-256 fingerprints of the Android app's signing certificates (uppercase hex pairs joined by colons), at most 10.")
  @example(#["14:6D:E9:83:C5:73:06:50:D8:EE:B9:95:2F:34:FC:64:16:A0:83:42:E6:1D:BE:A8:8A:04:96:B2:3F:CF:44:E5"])
  androidCertFingerprints: string[];

  @doc("How many passkeys can sign in now: those made for the current `rpId`.")
  @example(3)
  activePasskeyCount: int32;

  @doc("""
    The origins a passkey ceremony is accepted from today: the project's web platforms on `rpId` or its subdomains,
    `https://<rpId>` for iOS and macOS apps, and one `android:apk-key-hash:` origin per fingerprint. A wildcard web
    platform shows as a pattern, such as `https://*.example.com`, which accepts any one subdomain level.
    """)
  @example(#["https://app.example.com", "https://example.com"])
  acceptedOrigins: string[];

  @doc("""
    Whether every user must have a second factor (an authenticator app or a passkey) before they get a session. Sign
    up and password sign in then need a verified email, and a user with no factor enrolls one at their next sign in.
    Needs TOTP or passkeys on, and an email server.
    """)
  @example(false)
  mfaRequired: boolean;

  @doc("""
    How many users hold a live session and have no second factor, so would enroll one at their next sign in while
    `mfaRequired` is on. Counted by `consoleAuthMethods.get` only; null in the answer of an update.
    """)
  @example(4)
  activeUsersWithoutMfa: int32 | null;
}

@doc("Changes to a project's second factor and passkey settings. Fields left out keep their value.")
model UpdateAuthMethodSettingsRequest {
  @doc("Whether users can turn on an authenticator app.")
  @example(true)
  totpEnabled?: boolean;

  @doc("Whether users can add passkeys and sign in with them. Needs an `rpId`.")
  @example(true)
  passkeysEnabled?: boolean;

  @doc("The passkey domain; null clears it (only while passkeys are off).")
  @example("example.com")
  rpId?: string | null;

  @doc("The name authenticators show, 1 to 64 characters; null means the project name.")
  @example("Acme")
  rpName?: string | null;

  @doc("At most 10 SHA-256 fingerprints, uppercase or lowercase hex pairs joined by colons; stored uppercase.")
  androidCertFingerprints?: string[];

  @doc("""
    Whether every user must have a second factor before they get a session. Needs `totpEnabled` or `passkeysEnabled`,
    and turning it on needs an email server.
    """)
  @example(true)
  mfaRequired?: boolean;

  @doc("Must be true to change `rpId` while passkeys are registered under the current one: they stop working.")
  @example(true)
  confirmRpIdChange?: boolean;
}

@route("/v1/console/project/auth/methods")
@tag("console")
@useAuth(consoleSession)
interface ConsoleAuthMethodsApi {
  @doc("Gets the project's second factor and passkey settings; any member.")
  @operationId("consoleAuthMethods.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthMethods")
  @get
  get(): AuthMethodSettings | Problem;

  @doc("Changes the project's second factor and passkey settings; owners and developers only.")
  @operationId("consoleAuthMethods.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthMethods")
  @patch(#{ implicitOptionality: false })
  update(@body request: UpdateAuthMethodSettingsRequest): AuthMethodSettings | Problem;
}
