// API keys (spec 0003, row 7). The secret is returned once, by `consoleApiKeys.create`.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "./members.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("""
  What an API key may do: `<resource>.<read|write>`. A `write` scope does not imply `read`. Each product
  adds its own scopes.
  """)
enum ApiKeyScope {
  @doc("Read a project's users.")
  usersRead: "users.read",

  @doc("Create, change, and delete a project's users.")
  usersWrite: "users.write",
}

@doc("An API key for server code. Its secret is never shown again after creation.")
model ApiKey {
  @doc("The key ID.")
  @example("01924f5e-8d2b-7c4d-8e21-3d5e6f7a8b9c")
  id: string;

  @doc("A name to recognize the key by, 1 to 100 characters.")
  @example("Backend")
  name: string;

  @doc("The first 12 characters of the secret, to tell keys apart.")
  @example("orv_sk_Xy3aB")
  prefix: string;

  @doc("What the key may do.")
  scopes: ApiKeyScope[];

  @doc("When the key stops working; null for never.")
  expiresAt: utcDateTime | null;

  @doc("When the key was last used, to the minute; null if never.")
  lastUsedAt: utcDateTime | null;

  @doc("The console user who created the key.")
  @example("01924f5e-6b0a-7f2e-8c01-1b2c3d4e5f60")
  createdByUserId: string;

  @doc("The console user who created the key, with their name and email; null when that account no longer exists.")
  createdBy: ConsoleUserRef | null;

  @doc("When the key was created.")
  createdAt: utcDateTime;
}

@doc("One page of a project's API keys, oldest first.")
model ApiKeyPage {
  @doc("The keys on this page.")
  items: ApiKey[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new API key.")
model CreateApiKeyRequest {
  @doc("A name to recognize the key by; trimmed, 1 to 100 characters.")
  @example("Backend")
  name: string;

  @doc("What the key may do; at least one scope.")
  scopes: ApiKeyScope[];

  @doc("When the key stops working, in the future; null or absent for never.")
  expiresAt?: utcDateTime | null;
}

@doc("A new API key with its secret. The secret is shown only here, once.")
model CreatedApiKey {
  @doc("The key.")
  apiKey: ApiKey;

  @doc("The secret to send as the API key: `orv_sk_` plus 43 characters. Store it now; it can't be shown again.")
  @example("orv_sk_Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5a")
  secret: string;
}

@route("/v1/console/project/keys")
@tag("console")
@useAuth(consoleSession)
interface ConsoleApiKeysApi {
  @doc("Lists the API keys of the project named by `X-Orvano-Project`.")
  @operationId("consoleApiKeys.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleApiKeys")
  @get
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Keys per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): ApiKeyPage | Problem;

  @doc("""
    Creates an API key and returns its secret, once. Owners and developers; fails with `project_not_ready` while
    the project is being deleted, and `org_not_active` while the org is.
    """)
  @operationId("consoleApiKeys.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleApiKeys")
  @post
  create(@body request: CreateApiKeyRequest): {
    @statusCode statusCode: 201;
    @body key: CreatedApiKey;
  } | Problem;

  @doc("""
    Deletes an API key, which stops it working at once. Owners delete any key, developers their own; still allowed
    while the project is being deleted, but fails with `org_not_active` while the org is.
    """)
  @operationId("consoleApiKeys.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleApiKeys")
  @delete
  @route("/{keyId}")
  delete(
    @doc("The key ID.")
    @path
    keyId: string,
  ): NoContentResponse | Problem;
}
