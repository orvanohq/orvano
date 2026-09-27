// The console account's own session (spec 0004, AC-27): console accounts are users of project `console`. Sign up,
// sign in, refresh, and sign out set or clear two HttpOnly, SameSite=Strict cookies: orvano_console (the access
// token) and orvano_console_refresh (the refresh token, sent only to /v1/console/account/session). These four and
// consoleInstall.getSetup are the only console operations that need no console session.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("A new console account.")
model CreateConsoleAccountRequest {
  @doc("The email, trimmed, at most 320 characters. Unique among console accounts, ignoring case.")
  @example("ada@example.com")
  email: string;

  @doc("8 to 256 characters after Unicode NFKC normalization.")
  @example("correct horse battery staple")
  password: string;

  @doc("A display name, at most 256 characters.")
  @example("Ada Lovelace")
  name?: string | null;

  @doc("The token of the invitation this sign up came from, if any.")
  inviteToken?: string | null;

  @doc("The setup token from the installer's setup link; needed only for the install's first account.")
  setupToken?: string | null;
}

@doc("A console sign in with an email and password.")
model CreateConsoleSessionRequest {
  @doc("The console account's email; case does not matter.")
  @example("ada@example.com")
  email: string;

  @doc("The console account's password.")
  @example("correct horse battery staple")
  password: string;
}

@route("/v1/console/account")
@tag("console")
interface ConsoleAccountApi {
  @doc("""
    Creates a console account and signs it in, setting the session cookies. The install's sign up policy decides
    who may: the first account (with the setup token when the install has one), anyone while sign up is open, or
    an invitation.
    """)
  @operationId("consoleAccount.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @post
  create(@body request: CreateConsoleAccountRequest): {
    @statusCode statusCode: 201;
    @body body: User;
  } | Problem;

  @doc("Signs a console account in with its email and password, setting the session cookies.")
  @operationId("consoleAccount.createSession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session")
  @post
  createSession(@body request: CreateConsoleSessionRequest): {
    @statusCode statusCode: 201;
    @body body: User;
  } | Problem;

  @doc("Trades the refresh cookie for a new pair of session cookies. The console client calls it after `token_expired`.")
  @operationId("consoleAccount.refreshSession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @extension("x-orvano-idempotent", true)
  @route("/session/refresh")
  @post
  refreshSession(): NoContentResponse | Problem;

  @doc("Signs out: ends the console session and clears both cookies.")
  @operationId("consoleAccount.deleteSession")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @route("/session")
  @delete
  deleteSession(): NoContentResponse | Problem;

  @doc("Gets the signed in console account.")
  @operationId("consoleAccount.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAccount")
  @useAuth(consoleSession)
  @get
  get(): User | Problem;
}
