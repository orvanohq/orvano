// The Orvano API contract (spec 0001). TypeSpec is the only thing people edit; SdkGen reads the
// compiled dist/openapi.json. Every operation carries an audience and a service group:
//
//   @extension("x-orvano-audience", "client" | "server" | "both" | "console")
//   @extension("x-orvano-service", "<service>")
//   @operationId("<service>.<method>")
//
// Every operation also returns `Problem` (errors.tsp), its `default` response. Test only operations
// carry @extension("x-orvano-test", true) and live under /v1/test/ or /v1/console/test/.
//
// info.version must equal the repo's VERSION file; SdkGen refuses to run otherwise.
import "@typespec/http";
import "@typespec/openapi";
import "@typespec/openapi3";
import "./errors.tsp";
import "./system/health.tsp";
import "./test/test.tsp";

using Http;
using OpenAPI;

@service(#{ title: "Orvano" })
@info(#{ version: "0.0.0" })
@doc("The Orvano API. Every path lives under /v1.")
namespace Orvano;
