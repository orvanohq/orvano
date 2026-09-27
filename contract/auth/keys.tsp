// A project's token signing keys (spec 0004): the JWKS and the discovery document, so any JWT library can check
// Orvano access tokens on its own. They need no credentials; the project is in the path.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("One public signing key, as a JSON Web Key (RFC 7517).")
model Jwk {
  @doc("The key type, `EC`.")
  @example("EC")
  kty: string;

  @doc("The curve, `P-256`.")
  @example("P-256")
  crv: string;

  @doc("The public point's x coordinate, base64url.")
  @example("f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU")
  x: string;

  @doc("The public point's y coordinate, base64url.")
  @example("x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0")
  y: string;

  @doc("The key ID, the `kid` in a token's header.")
  @example("AZJPXo0rfE2OIT1ebzqLnA")
  kid: string;

  @doc("The algorithm, `ES256`.")
  @example("ES256")
  alg: string;

  @doc("What the key is for, `sig`.")
  @example("sig")
  use: string;
}

@doc("A project's public signing keys: the active key and any key retired within the last 24 hours.")
model Jwks {
  @doc("The keys.")
  keys: Jwk[];
}

@doc("""
  The discovery document standard JWT libraries configure themselves from. Orvano is not an OpenID provider;
  this exists so tools that take an issuer URL find the keys. Its names are the standard snake case ones.
  """)
@extension("x-orvano-standard-names", true)
model OpenIdConfiguration {
  @doc("The issuer, the `iss` of every access token of the project.")
  @example("https://orvano.example.com/v1/projects/shop")
  issuer: string;

  @doc("Where the project's JWKS lives.")
  @example("https://orvano.example.com/v1/projects/shop/.well-known/jwks.json")
  jwks_uri: string;

  @doc("Always `[\"ES256\"]`.")
  id_token_signing_alg_values_supported: string[];

  @doc("Always `[\"public\"]`.")
  subject_types_supported: string[];

  @doc("Always `[\"token\"]`.")
  response_types_supported: string[];
}

@route("/v1/projects/{projectId}/.well-known")
@tag("keys")
interface KeysApi {
  @doc("Gets a project's public signing keys, to check its access tokens.")
  @operationId("keys.getJwks")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "keys")
  @route("/jwks.json")
  @get
  getJwks(@doc("The project ID.") @path projectId: string): Jwks | Problem;

  @doc("Gets a project's discovery document, for JWT libraries that configure themselves from the issuer.")
  @operationId("keys.getOpenIdConfiguration")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "keys")
  @route("/openid-configuration")
  @get
  getOpenIdConfiguration(@doc("The project ID.") @path projectId: string): OpenIdConfiguration | Problem;
}
