import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Whether the server is up, and which Orvano version it runs.")
model Health {
  @doc("Always `ok` when the server answers.")
  @example("ok")
  status: string;

  @doc("The Orvano server version, major.minor.patch.")
  @example("0.4.2")
  version: string;
}

@route("/v1/health")
@tag("health")
interface HealthApi {
  @doc("Checks that the server is up and returns its version.")
  @operationId("health.get")
  @extension("x-orvano-audience", "both")
  @extension("x-orvano-service", "health")
  @get
  get(): Health | Problem;
}
