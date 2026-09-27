// Console orgs (spec 0003, row 7): the orgs a console user belongs to, and the org lifecycle.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("An org's lifecycle state.")
enum OrgStatus {
  @doc("In use.")
  active,

  @doc("Deleted; restorable by an owner until `purgeAfter`, then purged with everything in it.")
  deleting,
}

@doc("A console member's one role in an org.")
enum OrgRole {
  @doc("Everything, including deleting projects, managing members, and the org itself.")
  owner,

  @doc("Creates and edits projects, API keys, and platforms; deletes only the keys they created.")
  developer,

  @doc("Reads only.")
  viewer,
}

@doc("An org: the owner of projects and the unit teammates join.")
model Org {
  @doc("The org ID.")
  @example("01924f5e-7c1a-7a3b-9f10-2c4d5e6f7a8b")
  id: string;

  @doc("The org name, 1 to 100 characters.")
  @example("Acme")
  name: string;

  @doc("Whether the org is in use or being deleted.")
  status: OrgStatus;

  @doc("The calling console user's role in this org.")
  role: OrgRole;

  @doc("When the org was deleted; null unless `deleting`.")
  deletedAt: utcDateTime | null;

  @doc("When a deleted org is purged for good; null unless `deleting`.")
  purgeAfter: utcDateTime | null;

  @doc("When the org was created.")
  createdAt: utcDateTime;

  @doc("When the org last changed.")
  updatedAt: utcDateTime;
}

@doc("One page of the caller's orgs, oldest first.")
model OrgPage {
  @doc("The orgs on this page.")
  items: Org[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new org. The caller becomes its owner.")
model CreateOrgRequest {
  @doc("The org name; trimmed, 1 to 100 characters.")
  @example("Acme")
  name: string;
}

@doc("Changes to an org.")
model UpdateOrgRequest {
  @doc("The new org name; trimmed, 1 to 100 characters.")
  @example("Acme Inc")
  name: string;
}

@route("/v1/console/orgs")
@tag("console")
@useAuth(consoleSession)
interface ConsoleOrgsApi {
  @doc("Lists the orgs the caller is a member of, deleting ones included.")
  @operationId("consoleOrgs.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @get
  list(
    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Orgs per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): OrgPage | Problem;

  @doc("Creates an org with the caller as its owner.")
  @operationId("consoleOrgs.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @post
  create(@body request: CreateOrgRequest): {
    @statusCode statusCode: 201;
    @body org: Org;
  } | Problem;

  @doc("Gets one org the caller is a member of.")
  @operationId("consoleOrgs.get")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @get
  @route("/{orgId}")
  get(
    @doc("The org ID.")
    @path
    orgId: string,
  ): Org | Problem;

  @doc("Renames an org. Owners only; fails with `org_not_active` while the org is deleting.")
  @operationId("consoleOrgs.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @patch(#{ implicitOptionality: false })
  @route("/{orgId}")
  update(
    @doc("The org ID.")
    @path
    orgId: string,

    @body request: UpdateOrgRequest,
  ): Org | Problem;

  @doc("""
    Deletes an org: it moves to `deleting` and is purged after the grace period. Owners only. Fails with
    `org_not_empty` while any of its projects is not deleting.
    """)
  @operationId("consoleOrgs.delete")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @delete
  @route("/{orgId}")
  delete(
    @doc("The org ID.")
    @path
    orgId: string,
  ): Org | Problem;

  @doc("Restores a deleting org before its purge. Owners only. Its projects are restored one by one.")
  @operationId("consoleOrgs.restore")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleOrgs")
  @extension("x-orvano-idempotent", true)
  @post
  @route("/{orgId}/restore")
  restore(
    @doc("The org ID.")
    @path
    orgId: string,
  ): Org | Problem;
}
