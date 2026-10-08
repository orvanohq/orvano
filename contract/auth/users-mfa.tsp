// MFA and passkeys of a project's users, for a server holding an API key and for the console (spec 0013, AC-27):
// resetting a user's MFA when they lost every factor, and listing and removing their passkeys.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./passkeys.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@route("/v1/users")
@tag("users")
@useAuth(apiKey)
interface UsersMfaApi {
  @doc("""
    Turns MFA off for a user who lost every factor: deletes their authenticator app and recovery codes and ends all
    their sessions. Their passkeys stay. A user without MFA is left as they are.
    """)
  @operationId("users.resetMfa")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/mfa/reset")
  @post
  resetMfa(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Lists a user's passkeys, oldest first, including inactive ones.")
  @operationId("users.listPasskeys")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.read")
  @route("/{userId}/passkeys")
  @get
  listPasskeys(
    @doc("The user ID.")
    @path
    userId: string,
  ): PasskeyList | Problem;

  @doc("Removes one of a user's passkeys. Sessions stay.")
  @operationId("users.deletePasskey")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @route("/{userId}/passkeys/{passkeyId}")
  @delete
  deletePasskey(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The passkey's ID.")
    @path
    passkeyId: string,
  ): NoContentResponse | Problem;
}

@route("/v1/console/project/users")
@tag("console")
@useAuth(consoleSession)
interface ConsoleUsersMfaApi {
  @doc("""
    Turns MFA off for a user of the project named by `X-Orvano-Project`: deletes their authenticator app and recovery
    codes and ends all their sessions. Passkeys stay. Owners and developers.
    """)
  @operationId("consoleUsers.resetMfa")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @extension("x-orvano-idempotent", true)
  @route("/{userId}/mfa/reset")
  @post
  resetMfa(
    @doc("The user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;

  @doc("Lists a user's passkeys, oldest first, including inactive ones. Every role.")
  @operationId("consoleUsers.listPasskeys")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/passkeys")
  @get
  listPasskeys(
    @doc("The user ID.")
    @path
    userId: string,
  ): PasskeyList | Problem;

  @doc("Removes one of a user's passkeys. Sessions stay. Owners and developers.")
  @operationId("consoleUsers.deletePasskey")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/passkeys/{passkeyId}")
  @delete
  deletePasskey(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The passkey's ID.")
    @path
    passkeyId: string,
  ): NoContentResponse | Problem;
}
