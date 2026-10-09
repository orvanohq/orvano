// The users of a project as the console manages them (spec 0004, AC-29): the same shapes as the users service, for
// the project named by X-Orvano-Project. Every role reads; owners and developers change; viewers get 403 forbidden.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";
import "./users.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("A project's token signing key as the console shows it; never the key itself.")
model SigningKey {
  @doc("The key ID, the `kid` of the tokens it signs.")
  @example("pXj2Yc0dQpu8Z2l4n1Ew0g")
  id: string;

  @doc("`active` signs new tokens; `retiring` only verifies them until `retireAfter`.")
  status: SigningKeyStatus;

  @doc("When the key was created.")
  createdAt: utcDateTime;

  @doc("When a retiring key leaves the JWKS; null for the active key.")
  retireAfter: utcDateTime | null;
}

@doc("What a signing key does.")
enum SigningKeyStatus {
  @doc("Signs new tokens.")
  active,

  @doc("Rotated out: still verifies the tokens it signed, until it is deleted.")
  retiring,
}

@doc("A project's signing keys, the active one first.")
model SigningKeys {
  @doc("The keys.")
  keys: SigningKey[];
}

@route("/v1/console/project/auth/keys")
@tag("console")
@useAuth(consoleSession)
interface ConsoleAuthKeysApi {
  @doc("Lists the token signing keys of the project named by `X-Orvano-Project`. Every role.")
  @operationId("consoleAuthKeys.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthKeys")
  @get
  list(): SigningKeys | Problem;

  @doc("""
    Rotates the project's signing key: a new key signs from now on, and the old one keeps verifying the tokens it
    signed for 24 hours, then leaves. Owners only.
    """)
  @operationId("consoleAuthKeys.rotate")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthKeys")
  @route("/rotate")
  @post
  rotate(): SigningKeys | Problem;
}

@route("/v1/console/project/users")
@tag("console")
@useAuth(consoleSession)
interface ConsoleUsersApi {
  @doc("Lists the users of the project named by `X-Orvano-Project`, newest first. Every role.")
  @operationId("consoleUsers.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @get
  list(
    @doc("Only users whose email starts with this, ignoring case.")
    @query
    email?: string,

    @doc("Only users with this status: `active` or `blocked`.")
    @query
    status?: string,

    @doc("Only users created after this time.")
    @query
    createdAfter?: utcDateTime,

    @doc("Only users created before this time.")
    @query
    createdBefore?: utcDateTime,

    @doc("Only verified users (true) or unverified users (false).")
    @query
    emailVerified?: boolean,

    @doc("Only users with MFA `on` or `off`. With the project's authenticator app switch off, every user counts as `off`.")
    @query
    mfa?: string,

    @doc("Only guests (true) or only permanent users (false).")
    @query
    anonymous?: boolean,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Users per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): UserPage | Problem;

  @doc("Gets a user.")
  @operationId("consoleUsers.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}")
  @get
  get(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Creates a user with an email and password. It does not sign them in.")
  @operationId("consoleUsers.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @post
  create(@body request: CreateUserRequest): {
    @statusCode statusCode: 201;
    @body body: User;
  } | Problem;

  @doc("Blocks a user: ends every session and refuses sign in. Blocking a blocked user changes nothing.")
  @operationId("consoleUsers.block")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/block")
  @post
  block(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Unblocks a user so they can sign in again. Their old sessions stay ended.")
  @operationId("consoleUsers.unblock")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/unblock")
  @post
  unblock(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Deletes a user with their password and sessions. It can't be undone.")
  @operationId("consoleUsers.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}")
  @delete
  delete(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Marks a user's email verified or unverified. Owners and developers.")
  @operationId("consoleUsers.updateEmailVerification")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/email-verification")
  @put
  updateEmailVerification(
    @doc("The user ID.")
    @path
    userId: string,

    @body request: UpdateEmailVerificationRequest,
  ): User | Problem;

  @doc("Emails a user a link that verifies their email. Owners and developers.")
  @operationId("consoleUsers.createVerification")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/verification")
  @post
  createVerification(
    @doc("The user ID.")
    @path
    userId: string,

    @body request: CreateUserVerificationRequest,
  ): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("Emails a user a password reset link. Owners and developers.")
  @operationId("consoleUsers.createRecovery")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/recovery")
  @post
  createRecovery(
    @doc("The user ID.")
    @path
    userId: string,

    @body request: CreateUserRecoveryRequest,
  ): {
    @statusCode statusCode: 202;
  } | Problem;

  @doc("Changes a user's email at once, without a confirmation email. Owners and developers.")
  @operationId("consoleUsers.updateEmail")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/email")
  @put
  updateEmail(
    @doc("The user ID.")
    @path
    userId: string,

    @body request: UpdateUserEmailRequest,
  ): User | Problem;

  @doc("Lists a user's active sessions, newest first. `current` is always false.")
  @operationId("consoleUsers.listSessions")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/sessions")
  @get
  listSessions(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Sessions per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): SessionPage | Problem;

  @doc("Ends every session of a user.")
  @operationId("consoleUsers.deleteSessions")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/sessions")
  @delete
  deleteSessions(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Ends one session of a user.")
  @operationId("consoleUsers.deleteSession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/sessions/{sessionId}")
  @delete
  deleteSession(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The session to end.")
    @path
    sessionId: string,
  ): NoContentResponse | Problem;
}
