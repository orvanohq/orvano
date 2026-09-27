// Console projects (spec 0003, row 7). Project scoped console operations take the project from
// the X-Orvano-Project header, like every project scoped call (spec 0001).
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "./orgs.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("A project's lifecycle state.")
enum ProjectStatus {
  @doc("Its database schema is being created; public calls get `project_not_ready`.")
  provisioning,

  @doc("Served.")
  active,

  @doc("Provisioning failed; an owner or developer can retry it.")
  failed,

  @doc("Deleted and not served; restorable by an owner until `purgeAfter`.")
  deleting,
}

@doc("A project: one app's isolated backend.")
model Project {
  @doc("The project ID, 20 characters of `a` to `z` and `0` to `9`. Sent as `X-Orvano-Project`.")
  @example("k3v9q2x7m1p8r4t6w0yz")
  id: string;

  @doc("The org that owns the project.")
  @example("01924f5e-7c1a-7a3b-9f10-2c4d5e6f7a8b")
  orgId: string;

  @doc("The project name, 1 to 100 characters.")
  @example("Shop")
  name: string;

  @doc("Where the project is in its lifecycle.")
  status: ProjectStatus;

  @doc("When the project was deleted; null unless `deleting`.")
  deletedAt: utcDateTime | null;

  @doc("When a deleted project is purged for good; null unless `deleting`.")
  purgeAfter: utcDateTime | null;

  @doc("When the purge ran out of attempts; an owner can retry it. Null otherwise.")
  purgeFailedAt: utcDateTime | null;

  @doc("When the project was created.")
  createdAt: utcDateTime;

  @doc("When the project last changed.")
  updatedAt: utcDateTime;
}

@doc("One page of an org's projects, oldest first.")
model ProjectPage {
  @doc("The projects on this page.")
  items: Project[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new project.")
model CreateProjectRequest {
  @doc("The project name; trimmed, 1 to 100 characters.")
  @example("Shop")
  name: string;
}

@doc("Changes to a project.")
model UpdateProjectRequest {
  @doc("The new project name; trimmed, 1 to 100 characters.")
  @example("Shop (production)")
  name: string;
}

@route("/v1/console/orgs/{orgId}/projects")
@tag("console")
@useAuth(consoleSession)
interface ConsoleOrgProjectsApi {
  @doc("Lists an org's projects, deleting ones included.")
  @operationId("consoleProjects.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @get
  list(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Projects per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): ProjectPage | Problem;

  @doc("""
    Creates a project in `provisioning`; it turns `active` once its database schema exists. Owners and
    developers; fails with `org_not_active` while the org is deleting.
    """)
  @operationId("consoleProjects.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @post
  create(
    @doc("The org ID.")
    @path
    orgId: string,

    @body request: CreateProjectRequest,
  ): {
    @statusCode statusCode: 201;
    @body project: Project;
  } | Problem;
}

@route("/v1/console/project")
@tag("console")
@useAuth(consoleSession)
interface ConsoleProjectApi {
  @doc("Gets the project named by `X-Orvano-Project`, in any state.")
  @operationId("consoleProjects.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @get
  get(): Project | Problem;

  @doc("Renames the project. Owners and developers.")
  @operationId("consoleProjects.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @patch(#{ implicitOptionality: false })
  update(@body request: UpdateProjectRequest): Project | Problem;

  @doc("""
    Deletes the project: it stops being served at once and is purged after the grace period, with its
    data, keys, platforms, and users. Owners only.
    """)
  @operationId("consoleProjects.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @delete
  delete(): Project | Problem;

  @doc("Restores a deleting project before its purge; it passes through `provisioning`. Owners only.")
  @operationId("consoleProjects.restore")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @post
  @route("/restore")
  restore(): Project | Problem;

  @doc("Retries provisioning a `failed` project. Owners and developers.")
  @operationId("consoleProjects.retryProvisioning")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @post
  @route("/retry-provisioning")
  retryProvisioning(): Project | Problem;

  @doc("Retries the purge of a deleting project whose purge ran out of attempts. Owners and install admins.")
  @operationId("consoleProjects.retryPurge")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleProjects")
  @post
  @route("/retry-purge")
  retryPurge(): Project | Problem;
}
