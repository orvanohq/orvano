// The signed in user's own account (spec 0004): sign up, sign in, and the current user. Project scoped:
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

  @doc("Whether the email has been verified.")
  @example(false)
  emailVerified: boolean;

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

  @doc("Gets the signed in user. A server can call it with a user's access token to check that the session is still active.")
  @operationId("account.get")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @get
  get(): User | Problem;
}
