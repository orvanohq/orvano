// The credentials the API accepts (spec 0004, AC-35). Each operation names the one it needs with @useAuth;
// the sign up, sign in, and refresh operations need none. The apiKey and consoleSession schemes join as the
// server and console operations adopt them.
import "@typespec/http";

using Http;

namespace Orvano;

@doc("A signed in user's access token: `Authorization: Bearer <access token>`, an ES256 JWT.")
model bearer is BearerAuth;
