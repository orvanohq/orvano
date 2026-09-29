// Org invitations (spec 0008, row 15): an owner invites an email with a role and gets a link to share, shown once.
// The link opens the console's /invite page, which previews the invitation without a session and joins with an
// existing account (accept) or a new one (consoleAccount.create with inviteToken). The token travels in request
// bodies only, never in a path or query string.
import "@typespec/http";
import "@typespec/openapi";
import "../errors.tsp";
import "../auth/security.tsp";
import "./orgs.tsp";
import "./members.tsp";

using Http;
using OpenAPI;

namespace Orvano;

@doc("Whether an invitation can still be accepted.")
enum InvitationStatus {
  @doc("Can be accepted until `expiresAt`.")
  pending,

  @doc("Past `expiresAt`: it can't be accepted, but can be resent or revoked.")
  expired,
}

@doc("An invitation to join an org. Its link is shown only once, when it is created.")
model Invitation {
  @doc("The invitation ID.")
  @example("01924f5e-9e3c-7d5e-9f32-4e6f7a8b9c0d")
  id: string;

  @doc("The email the invitation is for, as typed. Only a console account with this email, ignoring case, can use it.")
  @example("grace@example.com")
  email: string;

  @doc("The role the invitation grants.")
  role: OrgRole;

  @doc("The owner who created it; null when that console account no longer exists.")
  invitedBy: ConsoleUserRef | null;

  @doc("Whether it can still be accepted.")
  status: InvitationStatus;

  @doc("When the invitation stops working, 7 days after it was created.")
  expiresAt: utcDateTime;

  @doc("When the invitation was created.")
  createdAt: utcDateTime;
}

@doc("One page of an org's invitations, oldest first.")
model InvitationPage {
  @doc("The invitations on this page.")
  items: Invitation[];

  @doc("Pass it as `cursor` to get the next page; null on the last page.")
  nextCursor: string | null;
}

@doc("A new invitation.")
model CreateInvitationRequest {
  @doc("The email to invite; trimmed, at most 320 characters. Inviting an email again replaces its old invitation.")
  @example("grace@example.com")
  email: string;

  @doc("The role the invitation grants.")
  role: OrgRole;
}

@doc("A new invitation and its link. The link is shown only here, once.")
model CreatedInvitation {
  @doc("The invitation.")
  invitation: Invitation;

  @doc("The link to share with the invited person. Anyone holding it can see the invitation until it is used or expires.")
  @example("https://orvano.example.com/invite#Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5a")
  url: string;
}

@doc("An invitation token, from the fragment of an invite link.")
model InvitationTokenRequest {
  @doc("The token: 43 base64url characters.")
  @example("Xy3aBcD4eF5gH6iJ7kL8mN9oP0qR1sT2uV3wX4yZ5a")
  token: string;
}

@doc("What an invite link is for, shown before joining.")
model InvitationPreview {
  @doc("The org's ID.")
  @example("01924f5e-7c1a-7a3b-9f10-2c4d5e6f7a8b")
  orgId: string;

  @doc("The org's name.")
  @example("Acme")
  orgName: string;

  @doc("The role the invitation grants.")
  role: OrgRole;

  @doc("The email the invitation is for.")
  @example("grace@example.com")
  email: string;

  @doc("The inviter's name, else their email; null when that console account no longer exists.")
  @example("Ada Lovelace")
  invitedByName: string | null;

  @doc("When the invitation stops working.")
  expiresAt: utcDateTime;
}

@doc("The org an accepted invitation joined.")
model AcceptedInvitation {
  @doc("The org, with the caller's role in it.")
  org: Org;

  @doc("True when the caller was already a member; their role did not change.")
  alreadyMember: boolean;
}

@route("/v1/console/orgs/{orgId}/invitations")
@tag("console")
@useAuth(consoleSession)
interface ConsoleOrgInvitationsApi {
  @doc("Lists the org's invitations, pending and expired, oldest first. Owners only.")
  @operationId("consoleInvitations.list")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInvitations")
  @get
  list(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The `nextCursor` of the previous page.")
    @query
    cursor?: string,

    @doc("Invitations per page, 1 to 100. Defaults to 25.")
    @query
    limit?: int32,
  ): InvitationPage | Problem;

  @doc("""
    Invites an email to the org with a role and returns the link to share, once. Owners only. Inviting an email
    again replaces its old invitation, whose link stops working. Fails with `already_member` when that email's
    console account is already a member, and `invitation_limit` past 100 invitations.
    """)
  @operationId("consoleInvitations.create")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInvitations")
  @post
  create(
    @doc("The org ID.")
    @path
    orgId: string,

    @body request: CreateInvitationRequest,
  ): {
    @statusCode statusCode: 201;
    @body invitation: CreatedInvitation;
  } | Problem;

  @doc("Revokes an invitation, so its link stops working at once. Owners only.")
  @operationId("consoleInvitations.revoke")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInvitations")
  @delete
  @route("/{invitationId}")
  revoke(
    @doc("The org ID.")
    @path
    orgId: string,

    @doc("The invitation ID.")
    @path
    invitationId: string,
  ): NoContentResponse | Problem;
}

@route("/v1/console/invitations")
@tag("console")
interface ConsoleInvitationsApi {
  @doc("""
    Shows what an invite link is for. Needs no console session, only the token. Fails with
    `invitation_not_found` for a used, replaced, or revoked link, and `invitation_expired` after it expired.
    """)
  @operationId("consoleInvitations.preview")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInvitations")
  @post
  @route("/preview")
  preview(@body request: InvitationTokenRequest): InvitationPreview | Problem;

  @doc("""
    Joins the org with the signed in console account, whose email must be the invitation's (ignoring case), and
    uses the invitation up. A member already in the org keeps their role.
    """)
  @operationId("consoleInvitations.accept")
  @extension("x-orvano-audience", "console")
  @extension("x-orvano-service", "consoleInvitations")
  @useAuth(consoleSession)
  @post
  @route("/accept")
  accept(@body request: InvitationTokenRequest): AcceptedInvitation | Problem;
}
