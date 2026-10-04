// Provider identities (spec 0012): native ID token sign in, linking and unlinking providers, the servers' and the
// console's view of a user's identities, and each project's provider settings in the console.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "./security.tsp";
import "./account.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("A provider account linked to a user: one way the user signs in.")
model Identity {
  @doc("The identity ID.")
  @example("01924f5e-aa1b-7c4d-8e21-3d5e6f7a8b9c")
  id: string;

  @doc("The provider.")
  provider: OAuthProvider;

  @doc("The provider's ID for the account: Google's and Apple's `sub`, GitHub's user ID, Microsoft's `<tid>:<oid>`.")
  @example("109876543210987654321")
  subject: string;

  @doc("The email the provider gave, verified or not; null when it gave none.")
  @example("ada@example.com")
  email: string | null;

  @doc("Whether the provider vouches for that email.")
  @example(true)
  emailVerified: boolean;

  @doc("When the provider was linked.")
  createdAt: utcDateTime;

  @doc("When the user last signed in with it; null if never.")
  lastSignInAt: utcDateTime | null;
}

@doc("A user's identities, oldest first: at most one per provider.")
model IdentityList {
  @doc("The identities.")
  items: Identity[];
}

@doc("A sign in with a provider's ID token from a native app.")
model CreateIdTokenSessionRequest {
  @doc("The provider that issued the token.")
  provider: IdTokenProvider;

  @doc("The provider's ID token, at most 8 KB.")
  @example("eyJhbGciOiJSUzI1NiIsImtpZCI6Ii4uLiJ9.e30.c2ln")
  idToken: string;

  @doc("""
    The raw nonce, 16 to 128 characters. Give the provider its lowercase hex SHA-256 (the SDKs' `createNonce` makes
    both), so the token carries that hash.
    """)
  @example("q2lV7l3o0W9nE5w6Xk0wq1JmYp8tVb3Z")
  nonce: string;

  @doc("Apple only, and required for Apple: the authorization code Sign in with Apple returned with the token.")
  @example("c1f2e3d4...")
  authorizationCode?: string;

  @doc("Apple only: the name Sign in with Apple returned on the first authorization, at most 256 characters.")
  @example("Ada Lovelace")
  name?: string | null;
}

@doc("A link of a provider to the signed in user, with the code a link flow returned.")
model CompleteOAuthLinkRequest {
  @doc("The `orvano_code` parameter Orvano added to your redirect URL. It works once, for 2 minutes.")
  @example("orv_oc_Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmE")
  code: string;

  @doc("The PKCE verifier whose challenge started the link flow.")
  @example("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
  codeVerifier: string;
}

@route("/v1/account")
@tag("account")
interface AccountIdentityApi {
  @doc("""
    Signs a user in with a provider's ID token from a native app (Google or Apple sign in), and with Apple's
    authorization code. It finds the user by the provider account, else by the provider's verified email, else
    creates them. Each token works once.
    """)
  @operationId("account.createIdTokenSession")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @extension("x-orvano-session", "start")
  @route("/sessions/id-token")
  @post
  createIdTokenSession(@body request: CreateIdTokenSessionRequest): {
    @statusCode statusCode: 201;
    @body body: AuthResult;
  } | Problem;

  @doc("""
    Starts linking a provider to the signed in user, like `createOAuthFlow`. The session must be at most 10 minutes
    old. The SDKs' `linkIdentity` does all of it.
    """)
  @operationId("account.createOAuthLinkFlow")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/identities/oauth/flows")
  @post
  createOAuthLinkFlow(@body request: CreateOAuthFlowRequest): OAuthFlow | Problem;

  @doc("Links the provider with the code a link flow returned and its verifier. Only the user who started the flow can finish it.")
  @operationId("account.completeOAuthLink")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/identities/oauth")
  @post
  completeOAuthLink(@body request: CompleteOAuthLinkRequest): {
    @statusCode statusCode: 201;
    @body body: Identity;
  } | Problem;

  @doc("Links a provider to the signed in user with its ID token from a native app. The session must be at most 10 minutes old.")
  @operationId("account.createIdTokenIdentity")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/identities/id-token")
  @post
  createIdTokenIdentity(@body request: CreateIdTokenSessionRequest): {
    @statusCode statusCode: 201;
    @body body: Identity;
  } | Problem;

  @doc("Lists the signed in user's identities, oldest first.")
  @operationId("account.listIdentities")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/identities")
  @get
  listIdentities(): IdentityList | Problem;

  @doc("Unlinks one of the signed in user's identities. The last way to sign in can't be removed. Sessions stay.")
  @operationId("account.deleteIdentity")
  @extension("x-orvano-audience", "client")
  @extension("x-orvano-service", "account")
  @useAuth(bearer)
  @route("/identities/{identityId}")
  @delete
  deleteIdentity(
    @doc("The identity to unlink.")
    @path
    identityId: string,
  ): NoContentResponse | Problem;
}

@route("/v1/users")
@tag("users")
@useAuth(apiKey)
interface UsersIdentityApi {
  @doc("Lists a user's identities, oldest first.")
  @operationId("users.listIdentities")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.read")
  @route("/{userId}/identities")
  @get
  listIdentities(
    @doc("The user ID.")
    @path
    userId: string,
  ): IdentityList | Problem;

  @doc("Unlinks one of a user's identities. The user's last way to sign in can't be removed. Sessions stay.")
  @operationId("users.deleteIdentity")
  @extension("x-orvano-audience", "server")
  @extension("x-orvano-service", "users")
  @extension("x-orvano-scope", "users.write")
  @route("/{userId}/identities/{identityId}")
  @delete
  deleteIdentity(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The identity to unlink.")
    @path
    identityId: string,
  ): NoContentResponse | Problem;
}

@route("/v1/console/project/users")
@tag("console")
@useAuth(consoleSession)
interface ConsoleUsersIdentityApi {
  @doc("Lists a user's identities, oldest first.")
  @operationId("consoleUsers.listIdentities")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/identities")
  @get
  listIdentities(
    @doc("The user ID.")
    @path
    userId: string,
  ): IdentityList | Problem;

  @doc("Unlinks one of a user's identities; owners and developers only. The user's last way to sign in can't be removed.")
  @operationId("consoleUsers.deleteIdentity")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleUsers")
  @route("/{userId}/identities/{identityId}")
  @delete
  deleteIdentity(
    @doc("The user ID.")
    @path
    userId: string,

    @doc("The identity to unlink.")
    @path
    identityId: string,
  ): NoContentResponse | Problem;
}

@doc("One provider's settings for the project. Secrets are write only: only whether they are set, and a hint, come back.")
model OAuthProviderSettings {
  @doc("The provider.")
  provider: OAuthProvider;

  @doc("Whether users may sign in with it.")
  @example(true)
  enabled: boolean;

  @doc("Google's web client ID, Apple's Services ID, GitHub's client ID, or Microsoft's application ID; null when not set.")
  @example("1234567890-abc.apps.googleusercontent.com")
  clientId: string | null;

  @doc("Whether the client secret is set (Google, GitHub, Microsoft).")
  @example(true)
  clientSecretSet: boolean;

  @doc("The client secret's last 4 characters; null when it is not set.")
  @example("1a2b")
  clientSecretHint: string | null;

  @doc("Google and Apple: the client IDs native apps sign in with (Android and iOS client IDs, bundle IDs).")
  clientIdsExtra: string[];

  @doc("Apple: the team ID; null when not set.")
  @example("ABCDE12345")
  appleTeamId: string | null;

  @doc("Apple: the Sign in with Apple key's ID; null when not set.")
  @example("KEY1234567")
  appleKeyId: string | null;

  @doc("Apple: whether the private key (.p8) is set.")
  @example(true)
  applePrivateKeySet: boolean;

  @doc("Microsoft: `common`, `organizations`, `consumers`, or a tenant GUID; null for the other providers.")
  @example("common")
  microsoftTenant: string | null;

  @doc("Whether everything the browser redirect flow needs is set.")
  @example(true)
  redirectReady: boolean;

  @doc("Whether everything native ID token sign in needs is set (Google and Apple only).")
  @example(false)
  nativeReady: boolean;

  @doc("The callback URL to register at the provider.")
  @example("https://orvano.example.com/v1/projects/my-project/oauth/google/callback")
  callbackUrl: string;

  @doc("When the settings last changed; null when never saved.")
  updatedAt: utcDateTime | null;
}

@doc("The four providers' settings, in the order the console shows them.")
model OAuthProviderSettingsList {
  @doc("Google, Apple, GitHub, and Microsoft.")
  items: OAuthProviderSettings[];
}

@doc("""
  One provider's whole settings. A secret field left out keeps the stored secret, null clears it, and a string
  replaces it. Every other field left out is cleared.
  """)
model UpdateOAuthProviderRequest {
  @doc("Whether users may sign in with it. An enabled provider needs the settings of at least one way to sign in.")
  @example(true)
  enabled: boolean;

  @doc("1 to 255 characters with no whitespace.")
  @example("1234567890-abc.apps.googleusercontent.com")
  clientId?: string | null;

  @doc("Google, GitHub, Microsoft: 8 to 1,024 characters. Write only.")
  @example("GOCSPX-abcdefghijklmnop")
  clientSecret?: string | null;

  @doc("Google and Apple: at most 10 native client IDs.")
  clientIdsExtra?: string[];

  @doc("Apple: exactly 10 capital letters or digits.")
  @example("ABCDE12345")
  appleTeamId?: string | null;

  @doc("Apple: exactly 10 capital letters or digits.")
  @example("KEY1234567")
  appleKeyId?: string | null;

  @doc("Apple: the .p8 file's contents, a PKCS#8 EC P-256 key of at most 8 KB. Write only.")
  applePrivateKey?: string | null;

  @doc("Microsoft: `common` (the default), `organizations`, `consumers`, or a tenant GUID.")
  @example("common")
  microsoftTenant?: string | null;
}

@route("/v1/console/project/auth/providers")
@tag("console")
@useAuth(consoleSession)
interface ConsoleAuthProvidersApi {
  @doc("Lists the project's settings for all four providers.")
  @operationId("consoleAuthProviders.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthProviders")
  @get
  list(): OAuthProviderSettingsList | Problem;

  @doc("Saves one provider's settings; owners and developers only.")
  @operationId("consoleAuthProviders.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthProviders")
  @route("/{provider}")
  @put
  update(
    @doc("The provider: `google`, `apple`, `github`, or `microsoft`.")
    @path
    provider: string,

    @body request: UpdateOAuthProviderRequest,
  ): OAuthProviderSettings | Problem;

  @doc("Removes one provider's settings and secrets; owners and developers only.")
  @operationId("consoleAuthProviders.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleAuthProviders")
  @route("/{provider}")
  @delete
  delete(
    @doc("The provider: `google`, `apple`, `github`, or `microsoft`.")
    @path
    provider: string,
  ): NoContentResponse | Problem;
}
