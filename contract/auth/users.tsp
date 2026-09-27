// The users of a project, for a server holding an API key (spec 0004, users service). Each operation names the
// API key scope it needs in x-orvano-scope.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("One page of a project's users, newest first.")
model UserPage {
  @doc("The users on this page.")
  items: User[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new user with an email and password, created by a server. No session is created.")
model CreateUserRequest {
  @doc("The email, trimmed, at most 320 characters. Unique in the project, ignoring case.")
  @example("ada@example.com")
  email: string;

  @doc("8 to 256 characters after Unicode NFKC normalization.")
  @example("correct horse battery staple")
  password: string;

  @doc("A display name, at most 256 characters.")
  @example("Ada Lovelace")
  name?: string | null;
}

@route("/v1/users")
@tag("users")
@useAuth(apiKey)
interface UsersApi {
  @doc("Lists the project's users, newest first.")
  @operationId("users.list")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.read")
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

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Users per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): UserPage | Problem;

  @doc("Gets a user.")
  @operationId("users.get")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.read")
  @route("/{userId}")
  @get
  get(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Creates a user with an email and password. It does not sign them in.")
  @operationId("users.create")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @post
  create(@body request: CreateUserRequest): {
    @statusCode statusCode: 201;
    @body body: User;
  } | Problem;

  @doc("Blocks a user: ends every session and refuses sign in. Blocking a blocked user changes nothing.")
  @operationId("users.block")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/block")
  @post
  block(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Unblocks a user so they can sign in again. Their old sessions stay ended.")
  @operationId("users.unblock")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/unblock")
  @post
  unblock(
    @doc("The user ID.")
    @path
    userId: string,
  ): User | Problem;

  @doc("Deletes a user with their password and sessions. It can't be undone.")
  @operationId("users.delete")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @route("/{userId}")
  @delete
  delete(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Lists a user's active sessions, newest first. `current` is always false.")
  @operationId("users.listSessions")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.read")
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
  @operationId("users.deleteSessions")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @route("/{userId}/sessions")
  @delete
  deleteSessions(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Ends one session of a user.")
  @operationId("users.deleteSession")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
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
