// The credentials the API accepts (spec 0004, AC-35). Each operation names the one it needs with @useAuth;
// the sign up, sign in, and refresh operations need none. An apiKey operation also names its scope in
// x-orvano-scope. The consoleSession scheme joins as the console operations adopt it.
import "@typespec/http";

using Http;

namespace Orvano;

@doc("A signed in user's access token: `Authorization: Bearer <access token>`, an ES256 JWT.")
model bearer is BearerAuth;

@doc("A server's API key: `X-Orvano-Key: orv_sk_...`, holding the scopes the operation needs.")
model apiKey is ApiKeyAuth<ApiKeyLocation.header, "X-Orvano-Key">;
