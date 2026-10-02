// The signed in user's own account (spec 0004): sign up, sign in, self service, and sessions. Project scoped:
// every call carries X-Orvano-Project.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Whether a user may sign in.")
enum UserStatus {
  @doc("May sign in.")
  active,

  @doc("Blocked: every session ended and sign in is refused.")
  blocked,
}

@doc("A user of a project.")
model User {
  @doc("The user ID.")
  @example("01924f5e-8d2b-7c4d-8e21-3d5e6f7a8b9c")
  id: string;

  @doc("The email, as typed at sign up; null for a user without one.")
  @example("ada@example.com")
  email: string | null;

  @doc("Whether the email has been verified. Also the access token's `email_verified` claim.")
  @example(false)
  emailVerified: boolean;

  @doc("When the email was verified; null while it is not.")
  emailVerifiedAt: utcDateTime | null;

  @doc("The display name; null when none was given.")
  @example("Ada Lovelace")
  name: string | null;

  @doc("Whether the user may sign in.")
  status: UserStatus;

  @doc("Your own data about the user: a JSON object of at most 16 KB.")
  metadata: Record<unknown>;

  @doc("When the user signed up.")
  createdAt: utcDateTime;

  @doc("When the user last signed in; null if never.")
  lastSignInAt: utcDateTime | null;
}

@doc("""
  A session's tokens. Send the access token as `Authorization: Bearer`; trade the refresh token for a new pair
  before the access token expires. The SDKs do both for you.
  """)
model SessionTokens {
  @doc("An ES256 JWT, valid for 15 minutes.")
  @example("eyJhbGciOiJFUzI1NiIsImtpZCI6Ii4uLiJ9.e30.c2ln")
  accessToken: string;

  @doc("When the access token expires.")
  accessTokenExpiresAt: utcDateTime;

  @doc("Trades itself for a new pair once; keep it secret.")
  @example("orv_rt_AZJPXo0rfE2OIT1ebzqLnA.Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9v")
  refreshToken: string;

  @doc("When the session ends unless it refreshes first: 30 days idle, or 365 days after sign in.")
  refreshTokenExpiresAt: utcDateTime;

  @doc("The session ID, also the `sid` claim of the access token.")
  @example("01924f5e-9a1b-7c4d-8e21-3d5e6f7a8b9c")
  sessionId: string;
}

@doc("A signed in user and their new session.")
model AuthResult {
  @doc("The user.")
  user: User;

  @doc("The new session's tokens.")
  session: SessionTokens;

  @doc("Whether this call created the user: true from sign up, and from a magic link or email code for a new email.")
  @example(false)
  isNewUser: boolean;

  @doc("What happened to the verification email sign up was asked to send; null when none was asked for.")
  verificationEmail: VerificationEmailStatus | null;
}

@doc("What happened to the verification email of a sign up. The user and session are created whatever it says.")
enum VerificationEmailStatus {
  @doc("Queued; it arrives shortly.")
  queued,

  @doc("Neither the project nor the install has an SMTP server, so nothing was sent.")
  not_configured,

  @doc("A send limit was reached, so nothing was sent; ask again later with `account.createVerification`.")
  rate_limited,
}

@doc("""
  What an emailed link is for: the `orvano_type` parameter Orvano adds to every link it emails, beside
  `orvano_token`. The SDKs' link helpers (`redeemLink`, `handleLink`) read it to call the right operation.
  """)
enum EmailLinkType {
  @doc("Verifies the email (`account.verifyEmail`).")
  verification,

  @doc("Resets the password and signs in (`account.completeRecovery`).")
  recovery,

  @doc("Signs in (`account.createMagicLinkSession`).")
  magic_link,

  @doc("Confirms an email change (`account.confirmEmailChange`).")
  email_change,
}

@doc("A request to email a verification link to the signed in user.")
model CreateVerificationRequest {
  @doc("""
    Your page that receives the link: a host that is one of the project's web platforms (`http` only on `localhost`
    or `127.0.0.1`), or your app's own scheme (its iOS, Android, or macOS identifier). The link adds
    `orvano_type=verification` and `orvano_token` to it.
    """)
  @example("https://app.example.com/auth/callback")
  redirectUrl: string;
}

@doc("An email verification, with the token from the emailed link.")
model VerifyEmailRequest {
  @doc("The `orvano_token` parameter of the emailed link.")
  @example("orv_el_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  token: string;
}

@doc("A request to email a sign in link.")
model CreateMagicLinkRequest {
  @doc("The email to sign in as; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("""
    Your page that receives the link, on a host that is one of the project's web platforms (`http` only on
    `localhost` or `127.0.0.1`). The link adds `orvano_type=magic_link` and `orvano_token` to it.
    """)
  @example("https://app.example.com/auth/callback")
  redirectUrl: string;

  @doc("Whether the link may create a user for an email that has none. Defaults to true.")
  @example(true)
  createUser?: boolean;
}

@doc("A sign in with the token from a magic link.")
model CreateMagicLinkSessionRequest {
  @doc("The `orvano_token` parameter of the emailed link.")
  @example("orv_el_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  token: string;
}

@doc("A request to email a 6 digit sign in code.")
model CreateEmailCodeRequest {
  @doc("The email to sign in as; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("Whether the code may create a user for an email that has none. Defaults to true.")
  @example(true)
  createUser?: boolean;
}

@doc("A sign in with an emailed code.")
model CreateEmailCodeSessionRequest {
  @doc("The email the code was sent to; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("The 6 digit code from the email.")
  @example("428613")
  code: string;
}

@doc("A request to change the signed in user's email. It changes once the link sent to the new address is opened.")
model UpdateEmailRequest {
  @doc("The new email, trimmed, at most 320 characters.")
  @example("ada.king@example.com")
  email: string;

  @doc("""
    Your page that receives the confirmation link: a host that is one of the project's web platforms, or your app's
    own scheme. The link adds `orvano_type=email_change` and `orvano_token` to it.
    """)
  @example("https://app.example.com/auth/callback")
  redirectUrl: string;

  @doc("The user's current password. A user without one leaves it out, and must have signed in within 10 minutes.")
  @example("correct horse battery staple")
  password?: string;
}

@doc("An email change confirmation, with the token from the link sent to the new address.")
model ConfirmEmailChangeRequest {
  @doc("The `orvano_token` parameter of the emailed link.")
  @example("orv_el_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  token: string;
}

@doc("How a session began.")
enum SessionMethod {
  @doc("A sign in with an email and password.")
  password,

  @doc("Sign up.")
  sign_up,

  @doc("A magic link.")
  magic_link,

  @doc("An email code.")
  email_code,

  @doc("A password reset.")
  recovery,
}

@doc("A request to email a password reset link.")
model CreateRecoveryRequest {
  @doc("The email of the account; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("""
    Your page that receives the link, on a host that is one of the project's web platforms (`http` only on
    `localhost` or `127.0.0.1`). The link adds `orvano_type=recovery` and `orvano_token` to it.
    """)
  @example("https://app.example.com/auth/callback")
  redirectUrl: string;
}

@doc("A password reset, with the token from the emailed link.")
model CompleteRecoveryRequest {
  @doc("The `orvano_token` parameter of the emailed link.")
  @example("orv_el_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  token: string;

  @doc("The new password: 8 to 256 characters after Unicode NFKC normalization.")
  @example("another horse battery staple")
  password: string;
}

@doc("A new user with an email and password.")
model CreateAccountRequest {
  @doc("The email, trimmed, at most 320 characters. Unique in the project, ignoring case.")
  @example("ada@example.com")
  email: string;

  @doc("8 to 256 characters after Unicode NFKC normalization.")
  @example("correct horse battery staple")
  password: string;

  @doc("A display name, at most 256 characters.")
  @example("Ada Lovelace")
  name?: string | null;

  @doc("""
    When set, also emails a verification link that opens here: a host that is one of the project's web platforms, or
    your app's own scheme. `AuthResult.verificationEmail` says whether it was sent.
    """)
  @example("https://app.example.com/auth/callback")
  verificationRedirectUrl?: string;
}

@doc("Changes to the signed in user. A field left out stays as it is.")
model UpdateAccountRequest {
  @doc("A new display name of at most 256 characters, or null to remove it.")
  @example("Ada King")
  name?: string | null;

  @doc("New metadata, replacing the old: a JSON object of at most 16 KB.")
  metadata?: Record<unknown>;
}

@doc("A password change.")
model UpdatePasswordRequest {
  @doc("The user's current password. A user without one leaves it out, and must have signed in within 10 minutes.")
  @example("correct horse battery staple")
  currentPassword?: string;

  @doc("The new password: 8 to 256 characters after Unicode NFKC normalization.")
  @example("another horse battery staple")
  newPassword: string;
}

@doc("A request to delete the signed in user.")
model DeleteAccountRequest {
  @doc("The user's current password. A user without one leaves it out, and must have signed in within 10 minutes.")
  @example("correct horse battery staple")
  password?: string;
}

@doc("A trade of a refresh token for a new pair.")
model RefreshSessionRequest {
  @doc("The session's current refresh token.")
  @example("orv_rt_AZJPXo0rfE2OIT1ebzqLnA.Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9v")
  refreshToken: string;
}

@doc("An active session of a user: one signed in device or browser.")
model Session {
  @doc("The session ID.")
  @example("01924f5e-9a1b-7c4d-8e21-3d5e6f7a8b9c")
  id: string;

  @doc("When the user signed in.")
  createdAt: utcDateTime;

  @doc("When the session last traded its refresh token.")
  lastRefreshedAt: utcDateTime;

  @doc("The user agent that signed in, at most 512 characters; null when none was sent.")
  @example("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)")
  userAgent: string | null;

  @doc("The SDK that signed in, from `X-Orvano-SDK`; null when none was sent.")
  @example("orvano-js/0.1.0")
  sdk: string | null;

  @doc("The IP address last seen for the session; null when unknown.")
  @example("203.0.113.7")
  ipAddress: string | null;

  @doc("Whether this is the session making the call.")
  @example(true)
  current: boolean;

  @doc("How the session began.")
  method: SessionMethod;
}

@doc("One page of a user's active sessions, newest first.")
model SessionPage {
  @doc("The sessions on this page.")
  items: Session[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A sign in with an email and password.")
model CreatePasswordSessionRequest {
  @doc("The email the user signed up with; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("The user's password.")
  @example("correct horse battery staple")
  password: string;
}

@route("/v1/account")
@tag("account")
interface AccountApi {
  @doc("Signs a new user up with an email and password, and signs them in.")
  @operationId("account.create")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @post
  create(@body request: CreateAccountRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("Signs a user in with their email and password.")
  @operationId("account.createPasswordSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/password")
  @post
  createPasswordSession(@body request: CreatePasswordSessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Emails a password reset link, if the email belongs to an active user. The answer is the same 202 whether or not
    it does, so it never tells anyone which emails have accounts.
    """)
  @operationId("account.createRecovery")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @route("/recovery")
  @post
  createRecovery(@body request: CreateRecoveryRequest): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("""
    Sets a new password with the token from a reset link, and signs the user in. Every other session of the user
    ends, and the email counts as verified. The token works once.
    """)
  @operationId("account.completeRecovery")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/recovery/confirm")
  @post
  completeRecovery(@body request: CompleteRecoveryRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("Emails the signed in user a link that verifies their email.")
  @operationId("account.createVerification")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/verification")
  @post
  createVerification(@body request: CreateVerificationRequest): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("""
    Verifies a user's email with the token from a verification link. It needs no session and creates none; a client
    signed in as that user refreshes its session, so the access token's claim changes at once. The token works once.
    """)
  @operationId("account.verifyEmail")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "user")
  @route("/verification/confirm")
  @post
  verifyEmail(@body request: VerifyEmailRequest): User | Problem;

  @doc("""
    Emails a sign in link. An email without a user gets one that creates the user, unless `createUser` is false. The
    answer is the same 202 either way, so it never tells anyone which emails have accounts.
    """)
  @operationId("account.createMagicLink")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @route("/magic-link")
  @post
  createMagicLink(@body request: CreateMagicLinkRequest): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("Signs a user in with the token from a magic link, creating them when the link was for a new email. The token works once.")
  @operationId("account.createMagicLinkSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/magic-link")
  @post
  createMagicLinkSession(@body request: CreateMagicLinkSessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Emails a 6 digit sign in code that works for 10 minutes. An email without a user gets one that creates the user,
    unless `createUser` is false. The answer is the same 202 either way.
    """)
  @operationId("account.createEmailCode")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @route("/email-code")
  @post
  createEmailCode(@body request: CreateEmailCodeRequest): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("Signs a user in with an emailed code. After 5 wrong tries the code stops working; ask for a new one.")
  @operationId("account.createEmailCodeSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/email-code")
  @post
  createEmailCodeSession(@body request: CreateEmailCodeSessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Starts changing the signed in user's email: emails a confirmation link to the new address. The email changes
    when that link is opened.
    """)
  @operationId("account.updateEmail")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/email")
  @put
  updateEmail(@body request: UpdateEmailRequest): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("""
    Changes a user's email to the new address with the token from the confirmation link, and marks it verified. It
    needs no session and keeps every session. The token works once.
    """)
  @operationId("account.confirmEmailChange")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "user")
  @route("/email/confirm")
  @post
  confirmEmailChange(@body request: ConfirmEmailChangeRequest): User | Problem;

  @doc("Gets the signed in user. A server can call it with a user's access token to check that the session is still active.")
  @operationId("account.get")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @get
  get(): User | Problem;

  @doc("Changes the signed in user's name or metadata.")
  @operationId("account.update")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "user")
  @useAuth(bearer)
  @patch(#{ implicitOptionality: false })
  update(@body request: UpdateAccountRequest): User | Problem;

  @doc("Changes the signed in user's password and ends every other session of theirs.")
  @operationId("account.updatePassword")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/password")
  @put
  updatePassword(@body request: UpdatePasswordRequest): NoContentResponse | Problem;

  @doc("Deletes the signed in user with all their sessions. It can't be undone.")
  @operationId("account.delete")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "end")
  @useAuth(bearer)
  @route("/delete")
  @post
  delete(@body request: DeleteAccountRequest): NoContentResponse | Problem;

  @doc("Trades the current refresh token for a new pair. The old token keeps answering with the same pair for 10 seconds, so a retry is safe.")
  @operationId("account.refreshSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "refresh")
  @extension("x-orvano-idempotent", true)
  @route("/sessions/refresh")
  @post
  refreshSession(@body request: RefreshSessionRequest): SessionTokens | Problem;

  @doc("Lists the signed in user's active sessions, newest first.")
  @operationId("account.listSessions")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/sessions")
  @get
  listSessions(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Sessions per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): SessionPage | Problem;

  @doc("Signs out: ends the current session.")
  @operationId("account.deleteCurrentSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "end")
  @useAuth(bearer)
  @route("/sessions/current")
  @delete
  deleteCurrentSession(): NoContentResponse | Problem;

  @doc("Ends one of the signed in user's sessions.")
  @operationId("account.deleteSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/sessions/{sessionId}")
  @delete
  deleteSession(
    @doc("The session to end.")
    @path
    sessionId: string,
  ): NoContentResponse | Problem;

  @doc("Ends every session of the signed in user except the current one.")
  @operationId("account.deleteOtherSessions")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/sessions")
  @delete
  deleteOtherSessions(): NoContentResponse | Problem;
}
