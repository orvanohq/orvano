// Test only operations (spec 0001, AC-18). They prove the SDK conventions (errors, pagination,
// the console audience, events) before any product has them. SdkGen writes them only into the
// scenario runners and the server contract types; the server maps them only in `Test`.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";

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

@route("/v1/test")
@tag("test")
interface TestApi {
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
