// Test only operations (spec 0001, AC-18). They prove the SDK conventions (errors, pagination,
// the console audience, events) before any product has them. SdkGen writes them only into the
// scenario runners and the server contract types; the server maps them only in `Test`.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "../auth/account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("One fixed item in the `test.list` page.")
@extension("x-orvano-test", true)
model TestItem {
  @doc("`item-1` to `item-5`.")
  @example("item-1")
  id: string;
}

@doc("One page of `test.list`.")
@extension("x-orvano-test", true)
model TestItemPage {
  @doc("The items on this page.")
  items: TestItem[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("The answer to `test.consolePing`.")
@extension("x-orvano-test", true)
model TestConsolePing {
  @doc("Always `ok`.")
  @example("ok")
  status: string;
}

@doc("The payload of the `test.pinged` event.")
@extension("x-orvano-test", true)
@extension("x-orvano-event", "test.pinged")
model TestPinged {
  @doc("Any text.")
  @example("hello")
  message: string;

  @doc("When the ping happened.")
  at: utcDateTime;
}

@doc("The newest email Mailpit caught for an address, read from its text part.")
@extension("x-orvano-test", true)
model TestEmail {
  @doc("The subject line.")
  @example("Reset your password")
  subject: string;

  @doc("The `orvano_type` of the first link carrying `orvano_token`; null when there is none.")
  @example("recovery")
  type: string | null;

  @doc("The `orvano_token` of that link; null when there is none.")
  @example("orv_el_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  token: string | null;

  @doc("The first run of exactly 6 digits, when the email has no such link; null otherwise.")
  @example("428613")
  code: string | null;

  @doc("That link in full; null when there is none.")
  @example("http://localhost:3000/auth?orvano_type=recovery&orvano_token=orv_el_x")
  url: string | null;
}

@doc("A request for a native ID token from the fake sign in provider (spec 0012).")
@extension("x-orvano-test", true)
model TestCreateIdTokenRequest {
  @doc("Google or Apple.")
  provider: IdTokenProvider;

  @doc("The token's audience: a client ID or bundle ID.")
  @example("com.acme.app")
  aud: string;

  @doc("The provider account's subject.")
  @example("apple-001")
  sub: string;

  @doc("The email claim.")
  @example("ada@example.com")
  email?: string;

  @doc("The email_verified claim.")
  @example(true)
  emailVerified?: boolean;

  @doc("The nonce claim: the hashed nonce, as the app gives it to the provider.")
  @example("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")
  nonce: string;

  @doc("Seconds until the token expires; negative for one already expired. Defaults to 600.")
  @example(600)
  expiresIn?: int32;
}

@doc("A native ID token from the fake sign in provider.")
@extension("x-orvano-test", true)
model TestIdToken {
  @doc("The signed ID token.")
  idToken: string;

  @doc("Apple only: an authorization code the fake token endpoint accepts for this user; null for Google.")
  authorizationCode: string | null;
}

@doc("One call the fake Apple revoke endpoint received.")
@extension("x-orvano-test", true)
model TestAppleRevocation {
  @doc("The client ID the token was issued to.")
  @example("com.acme.app")
  clientId: string;

  @doc("The first 16 hex characters of the token's SHA-256, never the token.")
  @example("9f86d081884c7d65")
  tokenHint: string;

  @doc("When the call arrived.")
  receivedAt: utcDateTime;
}

@doc("The fake Apple revoke endpoint's calls, oldest first.")
@extension("x-orvano-test", true)
model TestAppleRevocationList {
  @doc("The calls.")
  items: TestAppleRevocation[];
}

@route("/v1/test")
@tag("test")
interface TestApi {
  @doc("Signs a native ID token as Google or Apple would, with the fake provider's key.")
  @operationId("test.createIdToken")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @post
  @route("/oauth/id-tokens")
  createIdToken(@body request: TestCreateIdTokenRequest): TestIdToken | Problem;

  @doc("Lists the calls the fake Apple revoke endpoint received after `after`.")
  @operationId("test.listAppleRevocations")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @get
  @route("/oauth/apple/revocations")
  listAppleRevocations(
    @doc("Only calls received after this time.")
    @query
    after?: utcDateTime,
  ): TestAppleRevocationList | Problem;

  @doc("Waits up to 15 seconds for the newest email to `to` that Mailpit caught after `after`, and reads it.")
  @operationId("test.getLatestEmail")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @get
  @route("/emails/latest")
  getLatestEmail(
    @doc("The recipient.")
    @query
    to: string,

    @doc("Only an email received after this time.")
    @query
    after?: utcDateTime,
  ): TestEmail | Problem;

  @doc("Always answers 409 with code `test_conflict`.")
  @operationId("test.conflict")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @post
  @route("/conflict")
  conflict(): NoContentResponse | Problem;

  @doc("Pages through five fixed items.")
  @operationId("test.list")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @get
  @route("/items")
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Items per page, 1 to 100. Defaults to 2.")
    @query
    limit?: int32,
  ): TestItemPage | Problem;
}

@route("/v1/console/test")
@tag("test")
@useAuth(consoleSession)
interface ConsoleTestApi {
  @doc("Answers `{ status: ok }` to a valid console session.")
  @operationId("test.consolePing")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "test")
  @extension("x-orvano-test", true)
  @get
  @route("/ping")
  consolePing(): TestConsolePing | Problem;
}
