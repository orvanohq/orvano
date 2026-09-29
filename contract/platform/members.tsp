// Org members (spec 0008, row 15): who is in an org and with which role. Any member lists them; owners change
// roles and remove members; every member may remove themselves (leave).
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "./orgs.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Whether a member's console account may sign in.")
enum MemberStatus {
  @doc("May sign in.")
  active,

  @doc("Blocked: the account can't sign in. A blocked owner still counts as an owner.")
  blocked,
}

@doc("A console account and the name and email it goes by, for showing who did something.")
model ConsoleUserRef {
  @doc("The console user ID.")
  @example("01924f5e-6b0a-7f2e-8c01-1b2c3d4e5f60")
  id: string;

  @doc("The display name; null when none was given.")
  @example("Ada Lovelace")
  name: string | null;

  @doc("The email.")
  @example("ada@example.com")
  email: string;
}

@doc("A member of an org.")
model Member {
  @doc("The member's console user ID.")
  @example("01924f5e-6b0a-7f2e-8c01-1b2c3d4e5f60")
  userId: string;

  @doc("The display name; null when none was given.")
  @example("Ada Lovelace")
  name: string | null;

  @doc("The email.")
  @example("ada@example.com")
  email: string;

  @doc("Whether the member's account may sign in.")
  status: MemberStatus;

  @doc("The member's role in this org.")
  role: OrgRole;

  @doc("When the member joined the org.")
  joinedAt: utcDateTime;
}

@doc("""
  One page of an org's members, oldest first. A member whose console account no longer exists is left out, so a
  page can hold fewer items than the limit, even none, while `nextCursor` is still set.
  """)
model MemberPage {
  @doc("The members on this page.")
  items: Member[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("Changes to a member.")
model UpdateMemberRequest {
  @doc("The member's new role.")
  role: OrgRole;
}

@route("/v1/console/orgs/{orgId}/members")
@tag("console")
@useAuth(consoleSession)
interface ConsoleMembersApi {
  @doc("Lists the org's members, oldest first. Any member.")
  @operationId("consoleMembers.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleMembers")
  @get
  list(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Members per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): MemberPage | Problem;

  @doc("""
    Changes a member's role. Owners only. Fails with `last_owner` when it would demote the org's last owner.
    Takes effect on the member's next request.
    """)
  @operationId("consoleMembers.update")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleMembers")
  @patch(#{ implicitOptionality: false })
  @route("/{userId}")
  update(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The member's console user ID.")
    @path
    userId: string,

    @body request: UpdateMemberRequest,
  ): Member | Problem;

  @doc("""
    Removes a member from the org, or lets a member leave when `userId` is their own. Owners remove anyone; every
    member may leave. Fails with `last_owner` when it would leave the org without an owner. The member's access
    ends on their next request; API keys they created keep working.
    """)
  @operationId("consoleMembers.remove")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleMembers")
  @delete
  @route("/{userId}")
  remove(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The member's console user ID.")
    @path
    userId: string,
  ): NoContentResponse | Problem;
}
