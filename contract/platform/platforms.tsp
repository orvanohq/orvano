// Platforms (spec 0003, row 7): the web hosts and app IDs allowed to use a project.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Where an app runs.")
enum PlatformType {
  @doc("A website; browser calls must come from a matching origin.")
  web,

  @doc("An Android app, by package name.")
  android,

  @doc("An iOS app, by bundle ID.")
  ios,

  @doc("A macOS app, by bundle ID.")
  macos,

  @doc("A Windows app, by any label.")
  windows,

  @doc("A Linux app, by any label.")
  linux,
}

@doc("An app allowed to use a project.")
model Platform {
  @doc("The platform ID.")
  @example("01924f5e-9e3c-7d5e-9f32-4e6f7a8b9c0d")
  id: string;

  @doc("Where the app runs.")
  type: PlatformType;

  @doc("A name to recognize it by, 1 to 100 characters.")
  @example("Storefront")
  name: string;

  @doc("""
    Web: a hostname (`app.example.com`), `*.` plus a hostname of at least two labels, `localhost`, or an IPv4
    address, matched on any scheme and port. Android: the package name. iOS and macOS: the bundle ID.
    Windows and Linux: a label.
    """)
  @example("app.example.com")
  identifier: string;

  @doc("When the platform was added.")
  createdAt: utcDateTime;

  @doc("When the platform last changed.")
  updatedAt: utcDateTime;
}

@doc("One page of a project's platforms, oldest first.")
model PlatformPage {
  @doc("The platforms on this page.")
  items: Platform[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new platform. Unique per project, type, and identifier, ignoring case.")
model CreatePlatformRequest {
  @doc("Where the app runs.")
  type: PlatformType;

  @doc("A name to recognize it by; trimmed, 1 to 100 characters.")
  @example("Storefront")
  name: string;

  @doc("The host pattern or app ID; see `Platform.identifier`.")
  @example("app.example.com")
  identifier: string;
}

@doc("Changes to a platform. Its type never changes.")
model UpdatePlatformRequest {
  @doc("A new name; trimmed, 1 to 100 characters.")
  @example("Storefront (staging)")
  name?: string;

  @doc("A new host pattern or app ID; see `Platform.identifier`.")
  @example("staging.example.com")
  identifier?: string;
}

@route("/v1/console/project/platforms")
@tag("console")
@useAuth(consoleSession)
interface ConsolePlatformsApi {
  @doc("Lists the platforms of the project named by `X-Orvano-Project`.")
  @operationId("consolePlatforms.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consolePlatforms")
  @get
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Platforms per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): PlatformPage | Problem;

  @doc("Adds a platform. Owners and developers.")
  @operationId("consolePlatforms.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consolePlatforms")
  @post
  create(@body request: CreatePlatformRequest): {
    @statusCode statusCode: 201;
    @body platform: Platform;
  } | Problem;

  @doc("Renames a platform or changes its identifier. Owners and developers.")
  @operationId("consolePlatforms.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consolePlatforms")
  @patch(#{ implicitOptionality: false })
  @route("/{platformId}")
  update(
    @doc("The platform ID.")
    @path
    platformId: string,

    @body request: UpdatePlatformRequest,
  ): Platform | Problem;

  @doc("Removes a platform. Owners and developers.")
  @operationId("consolePlatforms.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consolePlatforms")
  @delete
  @route("/{platformId}")
  delete(
    @doc("The platform ID.")
    @path
    platformId: string,
  ): NoContentResponse | Problem;
}
