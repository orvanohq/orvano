import "@typespec/http";
import "@typespec/openapi";

using Http;
using OpenAPI;

namespace Orvano;

@doc("""
  An RFC 9457 problem details body. Every operation declares it as its `default` response, sent
  as `application/problem+json`.
  """)
@error
model Problem {
  @header contentType: "application/problem+json";

  @doc("Identifies the error: `https://orvano.dev/errors/<code>`.")
  @example("https://orvano.dev/errors/not_found")
  type: string;

  @doc("The standard HTTP reason phrase for the status.")
  @example("Not Found")
  title: string;

  @doc("The HTTP status code.")
  @example(404)
  status: int32;

  @doc("A short, safe sentence about this occurrence, when the server has one.")
  detail?: string;

  @doc("Orvano's stable error code, from the `ErrorCode` catalog. SDKs treat it as a plain string.")
  @example("not_found")
  code: string;

  @doc("The request ID to quote when reporting a problem; also sent as `X-Request-Id`.")
  @example("4bf92f3577b34da6a3ce929d0e0e4736")
  requestId: string;
}

@doc("Every stable error code the server sends in `Problem.code`.")
enum ErrorCode {
  @doc("Any failure the server did not expect (500).")
  internal_error,

  @doc("No such route or resource (404).")
  not_found,

  @doc("A bad parameter or body (400).")
  invalid_request,

  @doc("A pagination cursor the server can't read (400).")
  invalid_cursor,

  @doc("A `/v1/console` call without a valid console session, or with an API key or app session (401).")
  console_session_required,

  @doc("`Test` environment only: a response broke the API contract (500).")
  contract_violation,
}

@doc("Error codes only the test operations send. Generated only into the scenario runners.")
@extension("x-orvano-test", true)
enum TestErrorCode {
  @doc("`test.conflict` always answers 409 with this code.")
  test_conflict,
}
