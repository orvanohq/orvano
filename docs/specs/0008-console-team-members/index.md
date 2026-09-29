# 0008. Console team members, invitations, and roles

**Date**: 2026-09-28
**Status**: In Progress

## Summary

This spec finishes scope row 15: org owners invite teammates, give them a role (owner, developer, or viewer), change that role, and remove them, and any member can leave. Orvano can't send email yet, so an invite is a link that the owner copies once and shares however they like. The link opens a `/invite` page where the teammate joins with the account they already have, or creates one. The row also ships the leftovers spec 0007 handed to it: real names in the API keys table, a `/sign-up` page for installs with open sign up, and an install settings screen where install admins switch sign up between invite only and open.

## Requirements

**User stories**:
- As an org owner, I want to invite a teammate by email with a role and get a link I can paste into any chat, so that I can add people even though my server sends no email.
- As an invited teammate, I want the link to show who invited me, to which org, and with which role, and to let me join with my existing account or a new one, so that joining takes one step.
- As an org owner, I want to change a member's role or remove them, knowing access ends at once, so that I control what each person can do.
- As any member, I want to see who else is in the org and to leave it myself, so that I know my team and can walk away cleanly.
- As an install admin, I want to switch console sign up between invite only and open, and people on an open install to have a sign up page, so that I choose how public my server is.
- As a developer, I want the API keys table to name who created each key, so that I know whom to ask before deleting one.

**Acceptance criteria** (the contract):

Invitations (API)
- **AC-1**: `consoleInvitations.create` (owners only) takes `email` (trimmed, at most 320 characters, matching spec 0004 AC-1's pattern, else 400 `invalid_request`) and `role`. It creates an invitation whose token is 32 random bytes, base64url without padding (43 characters), stored only as its SHA-256 hash, with `expires_at` = the database clock plus 7 days, and answers 201 with the invitation and `url` = the install's public base URL joined with `invite#<token>` (built with `PublicUrl`, so a trailing slash or a base path is handled). The token and the url appear in no other response, and in no log line, event, or problem details body. More than 60 creates an hour by one account get 429 `rate_limited`.
- **AC-2**: Creating an invitation for an email that already has one in the org (pending or expired, ignoring case) deletes the old one and inserts the new one in the same transaction, so the old link stops working at once. The same transaction also deletes the org's invitations that expired more than 30 days ago. The create locks the org row `FOR UPDATE` first, so two racing creates for the same email leave exactly one invitation and never answer 500.
- **AC-3**: Create fails, checked in this order: 400 `invalid_request` for a bad body; 404 `not_found` for non members; 403 `forbidden` for developers and viewers; 409 `org_not_active` while the org is `deleting`; 409 `already_member` when the email belongs to a console account (ignoring case) that is already a member; 409 `invitation_limit` when the org would hold more than 100 invitations (pending or expired) after the create.
- **AC-4**: `consoleInvitations.list` (owners only) returns the org's invitations oldest first, 25 per page by default, keyset paged on (`created_at`, `id`). Each item has `id`, `email`, `role`, `invitedBy` (`{ id, name, email }`, or null when that account no longer exists), `status` (`pending`, or `expired` when `expires_at` is at or before the database clock), `expiresAt`, and `createdAt`. `consoleInvitations.revoke` (owners only) deletes one and answers 204; an unknown ID gets 404 `not_found`, and a `deleting` org gets 409 `org_not_active` (spec 0003 AC-15). Developers and viewers get 403 `forbidden` on both.
- **AC-5**: `consoleInvitations.preview` needs no session. It takes the token in the request body and answers `orgId`, `orgName`, `role`, `email`, `invitedByName` (the inviter's name, else their email, else null when that account is gone), and `expiresAt`. A missing or empty token gets 400 `invalid_request`; a token that isn't exactly 43 base64url characters gets 404 `invitation_not_found` without being hashed. Then, in this order: an unknown, used, revoked, or replaced token gets 404 `invitation_not_found`; an expired one gets 410 `invitation_expired`; an invitation to a `deleting` org gets 409 `org_not_active`. More than 60 calls a minute from one IP get 429 `rate_limited` with `Retry-After`.
- **AC-6**: `consoleInvitations.accept` (console session) takes the token in the body. It reads the invitation by hash to learn its org, locks that org row `FOR UPDATE`, then runs one `DELETE ... WHERE token_hash = @h AND expires_at > now() AND lower(email) = lower(@callerEmail) RETURNING`. When that deletes a row, the same transaction inserts a membership with the invitation's role, writes the events, and answers 200 with `{ org, alreadyMember: false }` (the `Org` carries the new role); when the caller is already a member, it inserts nothing, leaves the role unchanged, and answers `{ org, alreadyMember: true }`. When it deletes nothing, a second read picks the answer in this order: 404 `invitation_not_found` (gone), 410 `invitation_expired`, 409 `org_not_active`, 403 `invitation_email_mismatch` (the invitation is kept). Token format errors answer as in AC-5. Of two racing accepts of one token, exactly one gets 200 and the other gets 404. More than 30 accept attempts in 15 minutes by one account (every attempt counts) get 429 `rate_limited`.
- **AC-7**: `consoleAccount.create` with an `inviteToken`, once the install has an install admin, checks the token in spec 0003's `AdmitAsync`, whatever the sign up mode: after the existing install settings lock it reads the invitation by hash, locks its org row, and runs AC-6's delete with the sign up email. A bad token, an expired one, a `deleting` org, or a different email answers exactly as AC-5 and AC-6 do, in the same order, and creates nothing. On success the account, its personal org (spec 0003, unchanged), and the membership with the invitation's role are created in the same transaction that deleted the invitation. While no install admin exists, the token is ignored and the first account rules of spec 0006 apply. A sign up with no `inviteToken` behaves exactly as spec 0003 AC-7 says.

Members (API)
- **AC-8**: `consoleMembers.list` (any member) returns the org's members oldest first by `joinedAt`, 25 per page by default, keyset paged on the membership's (`created_at`, `id`). Each item has `userId`, `name`, `email`, `status` (`active` or `blocked`), `role`, and `joinedAt`. A membership whose console account no longer exists is dropped from its page after paging, so a page can hold fewer items than the limit (even none) while `nextCursor` is still set. Non members get 404 `not_found`.
- **AC-9**: `consoleMembers.update` (owners only) sets a member's `role` and answers the updated member. Setting the role the member already has changes nothing and writes no event. Errors, in order: 400 for a bad body, 404 `not_found` for non members, 403 `forbidden` for developers and viewers (before the target is looked up), 409 `org_not_active`, 404 `not_found` for an unknown member, 409 `last_owner` when it would demote the last owner.
- **AC-10**: `consoleMembers.remove` answers 204. Owners may remove any member, and every member may remove themselves (leaving). Errors, in order: 404 `not_found` for non members (leaving included), 403 `forbidden` for a developer or viewer removing someone else (before the target is looked up), 409 `org_not_active`, 404 `not_found` for an unknown member, 409 `last_owner` when it would remove the last owner. From the removed account's next request on, every console call for that org or its projects gets 404 (`not_found` or `project_not_found`), with no sign out needed. API keys the member created keep working. Owners are counted by membership rows, as in spec 0003, blocked owners included.
- **AC-11**: These changes write an outbox event in the same transaction: `platform.invitation.created` (with `role`), `platform.invitation.revoked` (with `reason`: `revoked`, `replaced`, or `expired` for the 30 day cleanup), `platform.invitation.accepted`, `platform.member.added` (with `role`, and `invitationId` when it came from one), `platform.member.role_changed` (changed field `role`, with `from` and `to`), and `platform.member.removed` (with `reason`: `removed` or `left`). Payloads carry IDs, the actor, role values, reasons, and field names only, never an email, token, hash, or url.

Accounts and install (API)
- **AC-12**: `consoleAccount.get`, `consoleAccount.create`, and `consoleAccount.createSession` return a `ConsoleAccount`: every field of spec 0004's `User` plus `isInstallAdmin`.
- **AC-13**: `consoleInstall.getSetup` also returns `signupOpen`, true when `console_signup` is `open`. It stays sessionless and rate limited as spec 0006 set it.
- **AC-14**: `ApiKey` gains `createdBy`: `{ id, name, email }`, or null when the creator's console account no longer exists; `createdByUserId` stays. The keys list resolves every creator on a page in one batch, and `consoleApiKeys.create` fills `createdBy` from the caller.

Members page (console)
- **AC-15**: `orgNav` gains "Members" (`/orgs/$orgId/members`, Lucide `Users` icon, deliberately different from the project Users entry's `UsersRound`) between Projects and Settings, for every role. The page lists members in a `DataTable`, 25 per page with "Load more", with loading and error states, tolerating short or empty pages (AC-8). Columns: Name (the name, or the part of the email before `@` when there is none, plus a "You" badge on your row); Email; Role (a badge); Status (a "Blocked" badge only when blocked); Joined (relative time); and a row actions menu.
- **AC-16**: "Invite" sits in the page header. It is disabled for developers and viewers with "Owners only", and while the org is `deleting` with "Restore the org first". It opens a dialog with Email and Role (a radio group of Owner, Developer, Viewer, each with its one line description, Developer selected). `already_member` shows under Email; `invitation_limit` and other server errors show in the form alert, and the dialog stays open.
- **AC-17**: After 201 the same dialog switches to a "Share this invite link" step: the url in a `CodeBlock` with its copy button, "For <email> as <role>", "Expires <date>", and "This link won't be shown again. Use Resend to make a new one." Focus moves to the copy button. Escape, a click outside, the close button, and Done all close it. The url lives only in this step's component state: the create mutation (also used by Resend) has `gcTime: 0` and is reset as soon as the url is copied into that state, so the url is never in the query cache, the mutation cache, `localStorage`, `sessionStorage`, the address bar, a toast, a log, or an error message. After the dialog closes no DOM node contains it.
- **AC-18**: Owners see a "Pending invitations" section below the members table (developers and viewers neither see it nor fetch it): a `DataTable` with Email, Role, Invited by (name, else email, else "Deleted account"), Expires (the date, or an "Expired" badge), and two actions. Resend calls create again with the same email and role and opens the link step of AC-17. Revoke opens a destructive confirm (focus on Cancel) and removes the row on success. The empty state reads "No pending invitations".
- **AC-19**: Owners' row menus hold "Change role" and "Remove" on every other member's row. Every member's own row holds "Change role" (owners only) and "Leave org". Change role opens a dialog with the same radio group, Save disabled until the role differs; demoting yourself adds the warning "You'll lose owner rights in this org". Remove opens a destructive confirm that names the member and says their access ends at once and the API keys they created keep working. Leave opens a destructive confirm naming the org. On `last_owner` the dialog shows the server's message in its alert and stays open. While the org is `deleting`, Change role, Remove, Leave, Resend, and Revoke are disabled with "Restore the org first". After you change your own role, the org query and the org list are refreshed so owner actions follow at once. After you leave, a toast confirms, the org list is refreshed, and you land on your first remaining org, or `/orgs` when none remain.

Invite page (console)
- **AC-20**: `/invite` is a public route outside the shell. Like `/setup` (spec 0006), its `beforeLoad` reads the token from `location.hash` into a module variable, removes the hash from the address bar (`history.replaceState`), and keeps the captured token when the router loads the route again with no hash; the token reaches components only through the route context. A missing token shows "This invite link is incomplete. Open it again from the message you got." Preview runs as a query whose key holds no token (`['console', 'invitations', 'preview']`, `gcTime: 0`, no retry) and shows a loading state, then: 404 "This invite link isn't valid anymore. It may have been used, replaced, or revoked."; 410 "This invite expired. Ask an owner of the org for a new link."; 409 "This org is being deleted."; any other error, the error panel with Retry.
- **AC-21**: The page learns whether you are signed in from the account query marked `meta: { sessionOptional: true }`, which the global session error handler skips, so a 401 there means "signed out" and never redirects to sign in. When you are signed in and your email matches, the page shows "<inviter> invited you to join <org> as <role>" (or "You're invited to join <org> as <role>" when the inviter is gone) and a Join button. Join calls accept (a mutation with `gcTime: 0`), then shows a success toast ("You joined <org>", or "You're already a member of <org>"), refreshes the org list, and takes you to `/orgs/$orgId`. When your email differs, the page says "This invite is for <email>. You're signed in as <your email>." with a Sign out button, which ends the session, clears the query cache, and stays on the page in AC-22's state with the token kept.
- **AC-22**: When you are signed out, the page shows the same invite line and two tabs, "Create account" first and "Sign in". Create account uses the auth form with the email filled in and read only, sends the `inviteToken`, and on success clears the query cache (as a sign in does), shows the toast "You joined <org>", and lands on `/orgs/$orgId` of the inviting org (the `orgId` from the preview). A 409 `user_already_exists` shows in the form alert with a button that switches to the Sign in tab. Sign in has the email filled in (editable) and on success clears the query cache and shows AC-21's signed in state.

Sign up and install (console)
- **AC-23**: `/sign-up` is a public route. While `setupRequired` is true it redirects to `/setup`; when you are already signed in it redirects to `/`. When `signupOpen` is false it says "Sign up on this server is by invitation. Ask an org owner for an invite link." with a link to sign in. When true it shows the auth form's new sign up mode (title "Create your account"; fields Name (optional), Email, Password; button "Create account") and on success lands in the shell like a sign in does. If the server answers 403 `signup_closed` (the mode changed after the page loaded), the form alert shows the invite only message. The sign in page shows a "Create account" link to `/sign-up` only when `signupOpen` is true.
- **AC-24**: The account menu shows "Install settings", above Sign out, only when `isInstallAdmin` is true. It opens `/install`, a shell page in the same frame as `/orgs` (no org or project sidebar entries), with a "Console sign up" card: a radio group of "Invite only" (people join through invite links) and "Anyone can sign up" (with a warning that anyone who can reach this console can create an account), and Save, disabled until the choice changes. Save calls `consoleInstall.updateSettings` and confirms with a toast. The page shows a skeleton until your account has loaded; a non admin then sees the in shell not found screen.
- **AC-25**: The API keys table's Created by column shows "You" when `createdByUserId` equals your account ID, else `createdBy.name`, else `createdBy.email`, and "Deleted account" when `createdBy` is null; a skeleton until your account has loaded, as spec 0007 AC-12 says. "Teammate" is gone.

Quality
- **AC-26**: New pages set their document title (`Members · <org> · Orvano`, `Install settings · Orvano`, `Join <org> · Orvano` once the preview loads and `Invite · Orvano` before, `Sign up · Orvano`) and render headings through `PageHeading` (shell pages) or the auth layout's heading (public pages). Everything follows spec 0005: tokens only, keyboard reachable, focus trapped in dialogs and returned on close, field errors under the field, server errors in the form alert, and axe finds zero violations in both themes and both densities. The invite dialog with its link step, the change role dialog, the sign up form mode, and the invite page states have catalog examples in `src/dev/examples.tsx` and entries in `keyboard-scripts.ts`.
- **AC-27**: A Playwright test against the gateway (compose profile `console`), through the UI only: the fixture owner invites a new email as developer and reads the link from the link step; a second browser context opens the link, creates an account, and lands in the org; the owner changes that member to viewer; the viewer sees "Create key" disabled with "Developers and owners only"; the owner removes the member; the removed member's next navigation to the org shows the not found screen.

## Decision

**Chosen option**: Option 1: copy once invite links, a public `/invite` page that joins with an existing account or a new one, and an org Members page, with no email sending in this row.

Invitations are single use bearer links bound to one email. Every membership change runs under the org row lock, and every screen is built from spec 0005's parts.

**Implementation skills**: `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`)

## Rationale

Reasoning and options: see [rationale.md](rationale.md).

## Feature design

### Data model sketch

One new table in the Platform module, from spec 0003's sketch with two changes this spec decides: accepted and revoked rows are deleted rather than stamped, so `accepted_at` and `revoked_at` are gone and the partial unique index becomes a plain one.

| Table | Column | Type | Null | Notes |
|---|---|---|---|---|
| `platform_invitations` | `id` | uuid | no | PK |
| | `org_id` | uuid | no | FK `platform_orgs`; the unique index below leads with it, so it serves as the FK index |
| | `email` | text | no | trimmed, as typed, at most 320 chars |
| | `role` | text | no | `owner` \| `developer` \| `viewer` (CHECK) |
| | `token_hash` | bytea | no | SHA-256 of the token; UNIQUE |
| | `invited_by_user_id` | uuid | no | console user, no FK |
| | `expires_at` | timestamptz | no | `created_at` plus 7 days |
| | `created_at` | timestamptz | no | database clock |
| | | | | UNIQUE (`org_id`, `lower(email)`) |

- Relationships: org 1 to N invitations; the inviter is a console user by ID, with no cross module FK. Accepting deletes the invitation and inserts one `platform_memberships` row (spec 0003, unchanged).
- Retention: a row lives until it is accepted, revoked, or replaced; until 30 days after it expired, when the org's next invitation create deletes it (AC-2); or until its org is purged. `platform.org.purge` deletes nothing from this table today (`OrgJobs.PurgeOrgAsync` deletes memberships and the org only), so this row adds the invitations delete before the org delete; without it the foreign key would stall every org purge.
- Migration: `NNNN_platform_invitations.sql` in `server/migrations/platform/`, plus the `InvitationRow` mapping in `PlatformDbContext` (the drift check covers it).

No other table changes. `ConsoleAccount`, `signupOpen`, and `ApiKey.createdBy` are read time additions.

### State transitions

```
Invitation: (none) ──create (owner)──▶ pending ──expires_at passes──▶ expired (derived, never stored)
            pending | expired ──revoke (owner) | create again (replace)──▶ (row deleted)
            expired for 30 days ──next create in the org──▶ (row deleted)
            pending ──accept | invited sign up──▶ (row deleted, membership added unless already a member)
            any ──org purge──▶ (row deleted)
Membership: (none) ──accept | invited sign up | org create──▶ role r
            role r ──change role (owner, not the last owner demoted)──▶ role r'
            role r ──remove (owner) | leave (self), not the last owner──▶ (row deleted)
```

An expired invitation can't be accepted but can be resent (replaced) or revoked. Every transition except expiry is refused with 409 `org_not_active` while the org is `deleting` (spec 0003 AC-15).

### Module seams

`Orvano.Auth` already references `Orvano.Platform`, so Platform can't call Auth's `IUserDirectory` without a reference cycle. This row inverts it the same way the sign up hooks work:

| Contract | Owned by | Implemented by | Members | Used for |
|---|---|---|---|---|
| `IConsoleUserDirectory` | Platform (`Contracts/`) | Auth, wrapping its `UserDirectory` for project `console` | `GetManyAsync(IReadOnlyCollection<Guid> ids, ct)` → `ConsoleUserSummary` · `FindByEmailAsync(string email, ct)` → `ConsoleUserSummary?`, matched with Postgres `lower(email)` (the same function as Auth's unique index) | members list, invitation `invitedBy`, preview `invitedByName`, `ApiKey.createdBy`, `already_member`, the caller's email on accept |
| `ConsoleUserSummary` | Platform (`Contracts/`) | | `Id`, `Email` (non null; a console user with no email is treated as missing), `Name` (nullable), `Status` (a Platform owned `ConsoleUserStatus`: `Active` \| `Blocked`, mapped by Auth) | |
| `IInstallAdmins` | Platform (`Contracts/`) | Platform | `IsInstallAdminAsync(Guid userId, ct)` → `bool` | `ConsoleAccount.isInstallAdmin` in Auth's account endpoints |
| `IConsoleSignupPolicy.AdmitAsync` | Platform | Platform | same signature; `SignupAdmission` gains `InvitationNotFound`, `InvitationExpired`, `InvitationEmailMismatch`, and `OrgNotActive`, and `Admitted` gains `InvitationId`. The invitation delete happens here (AC-7), so a failure has a channel | invited sign up |
| `IConsoleAccountCreated.OnCreatedAsync` | Platform | Platform | unchanged; with an invite admission it inserts the membership (skipping it when already present) and writes `platform.member.added` and `platform.invitation.accepted` | invited sign up |

Auth registers its `IConsoleUserDirectory` in `AuthModule`; Platform resolves it from DI at request time, so the module order stays as it is. Auth's `AccountService` maps the four new admission outcomes to 404 `invitation_not_found`, 410 `invitation_expired`, 403 `invitation_email_mismatch`, and 409 `org_not_active` (today its `default` arm answers `signup_closed`). Platform gets its own copy of spec 0004's email rule in `Domain/` (it can't reach Auth's internal `EmailRule`). Platform's `FailureKind` gains `Gone`, which `ApiMapping.Problem` maps to 410.

### API surface

All operations are in the `console` audience (the private `@orvano/console-client`; no public SDK changes). New files: `contract/platform/members.tsp` and `contract/platform/invitations.tsp`. New error codes in `contract/errors.tsp`. `consoleInvitations.preview` joins the sessionless list in `server/src/Orvano.Server/Hosting/ConsoleSessions.cs` (`Open`), and the comments that call the four account operations and `getSetup` "the only" sessionless ones (`contract/auth/console-account.tsp`, `server/src/Orvano.Platform/AGENTS.md`) are updated. CSRF (spec 0004 AC-28) still applies to it.

| Operation | Method and path | Key inputs | Key outputs | Auth | Key errors |
|---|---|---|---|---|---|
| `consoleMembers.list` | GET `/v1/console/orgs/{orgId}/members` | `cursor?`, `limit?` (1 to 100, default 25) | `MemberPage` (`items: Member[]`, `nextCursor`) | member | 404 `not_found`, 400 `invalid_cursor` |
| `consoleMembers.update` | PATCH `/v1/console/orgs/{orgId}/members/{userId}` | `role` | `Member` | owner | AC-9 order |
| `consoleMembers.remove` | DELETE `/v1/console/orgs/{orgId}/members/{userId}` | | 204 | owner, or self | AC-10 order |
| `consoleInvitations.list` | GET `/v1/console/orgs/{orgId}/invitations` | `cursor?`, `limit?` | `InvitationPage` | owner | 404, 403, 400 `invalid_cursor` |
| `consoleInvitations.create` | POST `/v1/console/orgs/{orgId}/invitations` | `email`, `role` | 201 `CreatedInvitation` (`invitation`, `url`) | owner (rate limited per account) | AC-3 order, 429 |
| `consoleInvitations.revoke` | DELETE `/v1/console/orgs/{orgId}/invitations/{invitationId}` | | 204 | owner | 404, 403, 409 `org_not_active`, 404 unknown ID |
| `consoleInvitations.preview` | POST `/v1/console/invitations/preview` | `token` | `InvitationPreview` | none (rate limited per IP) | AC-5 order, 429 |
| `consoleInvitations.accept` | POST `/v1/console/invitations/accept` | `token` | `AcceptedInvitation` (`org`, `alreadyMember`) | console session (rate limited per account) | AC-6 order, 429 |
| `consoleAccount.get`, `.create`, `.createSession` (change) | as today | as today | `ConsoleAccount` (was `User`) | as today | as today, plus AC-7's codes on `create` |
| `consoleInstall.getSetup` (change) | GET `/v1/console/install/setup` | | `InstallSetup` + `signupOpen` | none | as today |
| `consoleApiKeys.list`, `.create` (change) | as today | as today | `ApiKey` + `createdBy` | as today | as today |

Models: `Member` (`userId`, `name: string | null`, `email`, `status: MemberStatus` = `active` \| `blocked`, `role: OrgRole`, `joinedAt`); `Invitation` (`id`, `email`, `role`, `invitedBy: ConsoleUserRef | null`, `status: InvitationStatus` = `pending` \| `expired`, `expiresAt`, `createdAt`); `ConsoleUserRef` (`id`, `name: string | null`, `email`), also used by `ApiKey.createdBy`; `CreatedInvitation` (`invitation`, `url`); `InvitationPreview` (`orgId`, `orgName`, `role`, `email`, `invitedByName: string | null`, `expiresAt`); `AcceptedInvitation` (`org: Org`, `alreadyMember: boolean`); `ConsoleAccount` (spec 0004's `User` fields plus `isInstallAdmin: boolean`). The token travels in request bodies only, never in a path or query string.

New error codes (all added by this row): `invitation_not_found` (404), `invitation_expired` (410), `invitation_email_mismatch` (403), `already_member` (409), `invitation_limit` (409).

New rate limit policies in `RateLimitPolicies`: `console.invite_create.user` (60 an hour), `console.invite_preview.ip` (60 a minute), and `console.invite_accept.user` (30 in 15 minutes). Each counts every attempt (`Acquire`), not only failures.

### Screens

| Route file | URL | Holds |
|---|---|---|
| `_app/orgs/$orgId/members.tsx` (new) | `/orgs/$orgId/members` | Members table, Invite dialog with link step, Pending invitations (owners), change role, remove, and leave dialogs |
| `invite.tsx` (new, public) | `/invite` | Preview states, signed in Join or email mismatch, signed out Create account and Sign in tabs |
| `sign-up.tsx` (new, public) | `/sign-up` | Redirects, the invite only message, or the sign up form |
| `sign-in.tsx` (change) | `/sign-in` | "Create account" link when `signupOpen` |
| `_app/install.tsx` (new) | `/install` | Console sign up card (install admins), in the `/orgs` frame |
| `-auth/auth-form.tsx` (change) | | A sign up mode, and an optional read only prefilled email |
| `shell/account-menu.tsx` (change) | | "Install settings" above Sign out, for install admins |
| `shell/nav.ts` (change) | | "Members" in `orgNav` |
| `projects/$projectId/-keys/columns.tsx` (change) | | Created by from `createdBy` |

Feature code sits beside its route (`-members/`, `-invite/`), per `console/AGENTS.md`. New query keys beside `src/lib/queries.ts`: `['console', 'orgs', orgId, 'members']` and `['console', 'orgs', orgId, 'invitations']` (both infinite, limit 25), `['console', 'invitations', 'preview']` (AC-20), and `['console', 'install', 'settings']`. The account query's type becomes `ConsoleAccount`. The global session error handler in `src/main.tsx` learns to skip queries marked `meta: { sessionOptional: true }` (used only by `/invite` and `/sign-up`).

After a member or invitation change, the page invalidates its own list; after a change to your own membership, it also invalidates the org query and the org list; after accept, leave, or an invited sign up, it invalidates the org list. A 404 from a member or invitation mutation (someone else already removed that member or invitation) shows an error toast with the server's message and refetches the list and the org query; only when the org query itself then answers 404 does the existing state moved handling (`src/lib/state-moved.ts`) take over.

Role descriptions in the radio groups come from the contract's `OrgRole` docs, added to the existing `console/src/lib/roles.tsx` as a map typed `Record<OrgRole, …>`, so a new role fails the build until it is described.

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| Create invitation | token | 32 bytes from `RandomNumberGenerator`, base64url without padding (spec 0003) |
| Create invitation | `token_hash` | SHA-256 of the token's UTF-8 bytes |
| Create invitation | `url` | `PublicUrl` (`ORVANO_PUBLIC_URL`, validated at start) joined with `invite#` plus the token |
| Create invitation | `expires_at` | database clock (`UnitOfWork.NowAsync`) plus 7 days (constant `Invitation.Lifetime`) |
| Create invitation | `invited_by_user_id` | the console session user |
| Create invitation | which old invitation to replace | row with (`org_id`, `lower(email)`) equal to the request's, read under the org lock |
| Create invitation | which expired invitations to clean up | the org's rows with `expires_at < now() - 30 days` (constant `Invitation.ExpiredRetention`) |
| Create invitation | already a member or not | `IConsoleUserDirectory.FindByEmailAsync(email)`, then `platform_memberships` for (`org_id`, that user) |
| Create invitation | the cap | `count(*)` of the org's invitations under the org lock, after the replace and the cleanup, plus one, must be at most 100 |
| List invitations | `status` | `expires_at <= now()` in the query (database clock) |
| List invitations | `invitedBy` | `IConsoleUserDirectory.GetManyAsync` over the page's `invited_by_user_id`s |
| Preview | `orgName`, `orgId` | `platform_orgs` row of the invitation |
| Preview | `invitedByName` | `IConsoleUserDirectory.GetManyAsync([invited_by_user_id])`: `Name`, else `Email`, else null |
| Accept | the caller's email | `IConsoleUserDirectory.GetManyAsync([session user])` |
| Accept, invited sign up | email match | `lower(invitation.email) = lower(caller email)` inside the consuming `DELETE` (Postgres `lower`) |
| Accept | `alreadyMember` | whether `platform_memberships` has (`org_id`, session user) under the org lock |
| Accept, invited sign up | the new membership's role | `platform_invitations.role` returned by the `DELETE` |
| Accept, invited sign up | which error when nothing was deleted | a second read of the row by `token_hash` and its org: none → 404, expired → 410, org `deleting` → 409, else 403 |
| Invited sign up | where the console lands | `orgId` from the preview the page already holds |
| Members list | `name`, `email`, `status` | `IConsoleUserDirectory.GetManyAsync` over the page's `user_id`s |
| Members list | `joinedAt` | `platform_memberships.created_at` |
| Members page | "You" badge, Leave on your row | `ConsoleAccount.id` equals `Member.userId` |
| Members page | whether to show owner actions and Pending invitations | `Org.role` of the current org (existing org query) |
| Members page | "Restore the org first" | `Org.status = deleting` (existing org query) |
| Leave | where you land | the first org of the refreshed `consoleOrgs.list`, else `/orgs` |
| Invite page | signed in or not | the account query with `sessionOptional`; 401 means signed out |
| Invite page | token | the route context, captured from `location.hash` in `beforeLoad` |
| `ConsoleAccount` | `isInstallAdmin` | `IInstallAdmins.IsInstallAdminAsync(user id)`: a row in `platform_install_admins` |
| `getSetup` | `signupOpen` | `platform_install_settings.console_signup = 'open'` |
| `ApiKey` | `createdBy` | `IConsoleUserDirectory.GetManyAsync` over the page's `created_by_user_id`s, null when missing; on create, the caller |
| Link step | "Expires <date>" | `invitation.expiresAt`, formatted in the browser's time zone (`src/lib/format.ts`) |
| Events | payload | IDs (`orgId`, `invitationId`, `membershipId`, `userId`), actor, role values, `reason`, changed field names |

### Key invariants

- Every change to an org's memberships or invitations (create, replace, cleanup, revoke, accept, invited sign up, role change, remove, leave) locks the org row `FOR UPDATE` before it writes. That is what keeps one owner in the org (spec 0003 AC-10), one invitation per email, the cap at 100, and one winner between racing accepts. Lock order is always the install settings row (sign up only) before the org row; nothing takes them the other way round.
- An invitation is consumed only by the conditional `DELETE ... WHERE token_hash AND expires_at > now() AND lower(email) = lower(@email) RETURNING`, taken after the org lock; the read by hash before the lock only finds the org, and the read after a zero row delete only picks the error. Both accept and invited sign up use this one path.
- The token exists only in the create response, the link the owner shares, and the invite page's memory. The database holds only its hash; no log, event, problem details body, URL path, query string, query key, or cache entry carries it.
- An invitation admits only the account whose email equals it, ignoring case.
- The invited role is applied exactly; accept never changes the role of an existing member.
- Removing a member or changing a role takes effect on the next request, because `IConsoleAccess` reads `platform_memberships` on every call (spec 0003). Nothing is cached.

### Security model

Console roles, per org, extending spec 0003's matrix:

| Action | owner | developer | viewer |
|---|---|---|---|
| See members (name, email, role, status, joined) | yes | yes | yes |
| See, create, resend, revoke invitations | yes | no | no |
| Invite with any role, owner included | yes | no | no |
| Change any member's role, their own included | yes (never leaving zero owners) | no | no |
| Remove another member | yes (never leaving zero owners) | no | no |
| Leave the org | yes (not as last owner) | yes | yes |

- Install admin: read and change `console_signup` (spec 0003, existing operations) and see "Install settings". It grants no org access.
- Public signals: `consoleInvitations.preview` needs only the token, is rate limited per IP, and reveals the org's name and ID, the role, the invited email, and the inviter's name, only to someone holding a live token. `getSetup.signupOpen` tells anyone whether the install has open sign up; a sign up attempt already reveals that, so it adds nothing new. `consoleAccount.create` keeps its sign up rate limit.
- A non member gets 404 on every member and invitation operation, so an org's existence never leaks (spec 0003 convention); a member without the role gets 403 `forbidden`, checked before the target member is looked up.
- Personal data: invitations hold the email of someone who may never join. It is deleted on accept, revoke, and replace, 30 days after expiry at the org's next invite, and on org purge, and never reaches an event or a log.
- CSRF: every new console operation rides the existing Fetch Metadata rule (spec 0004 AC-28); preview and accept are POST so the token never enters a URL.

### Configuration required

No new settings. The invite url uses `ORVANO_PUBLIC_URL`, which every role already validates at start (spec 0002).

### Critical test scenarios

- Happy path, new account: an owner invites `new@x` as developer; preview shows org, role, and inviter; sign up with the token creates the account, its personal org, and a developer membership, and deletes the invitation; the link then answers 404. Verifies **AC-1**, **AC-5**, **AC-7**.
- Happy path, existing account: a signed in account with the invited email accepts and becomes a member with the invited role; a second accept of the same link gets 404. Verifies **AC-6**.
- Replace and cleanup: inviting the same email again makes the first link answer 404 and leaves one row; two racing creates leave one row and no 500; an invitation expired 31 days ago is gone after the next create, and one expired 29 days ago stays. Verifies **AC-2**.
- Expiry: with the clock past `expires_at`, list shows `expired`, preview and accept get 410, and a resend works. Verifies **AC-4**, **AC-5**.
- Email mismatch and precedence: accept and invited sign up with another email get 403 and keep the invitation; an expired invitation with another email gets 410, not 403. Verifies **AC-6**, **AC-7**.
- Token format: an empty token gets 400, a 42 character token gets 404. Verifies **AC-5**.
- Concurrency: two accepts of one token race, exactly one membership results; an accept and an invited sign up of one token race, exactly one succeeds; two owners demoting each other race, one gets 409 `last_owner`. Verifies **AC-6**, **AC-7**, **AC-9**.
- Last owner: demote, remove, and leave as the only owner each get 409 `last_owner`. Verifies **AC-9**, **AC-10**.
- Access ends: after removal, the removed account's next org and project calls get 404, and a key it created still authenticates. Verifies **AC-10**.
- Org purge: an org with pending invitations purges cleanly and leaves no invitation rows. Verifies **AC-2** (retention).
- Auth/permission: a developer creating an invitation, listing invitations, changing a role, or removing someone else (even an unknown user ID) gets 403; a non member gets 404; a viewer can leave; everything but restore gets 409 in a `deleting` org. Verifies **AC-3**, **AC-4**, **AC-9**, **AC-10**.
- Leak checks: the token and url appear in no log line, event row, or problem body during every flow above; the url is in no query or mutation cache or storage after the link step closes; the token is in no query key or cache on the invite page. Verifies **AC-1**, **AC-11**, **AC-17**, **AC-20**.
- Invite page session: signed out, the page shows the tabs and never redirects to sign in; Sign out from the mismatch state keeps the token. Verifies **AC-21**, **AC-22**.
- Rate limits: the 61st preview in a minute from one IP gets 429. Verifies **AC-5**.
- End to end: the Playwright flow. Verifies **AC-27**.

## Build plan

Tracer Bullet: the first slice pushes one invitation from the owner's dialog through the contract, the server, and the invite page to a new member, end to end; later slices thicken it.

1. Thin thread, invite to join. Migration `NNNN_platform_invitations.sql` and `InvitationRow`, and the invitations delete in `OrgJobs.PurgeOrgAsync` with its test; domain types with unit tests (`InviteToken`: generate, format check, hash; `Invitation`: lifetime, retention, status; Platform's email rule); `IConsoleUserDirectory` and `ConsoleUserSummary` in Platform `Contracts/` with Auth's implementation; `FailureKind.Gone`; `invitations.tsp` with create, preview, and accept, and `members.tsp` with list, plus the five error codes; `InvitationService` (create with replace, cleanup, cap, and `already_member` under the org lock; preview; accept through the one consuming delete); `AdmitAsync` and `OnCreatedAsync` taking invite tokens, and Auth's mapping of the new outcomes; `consoleMembers.list`; preview in `ConsoleSessions.Open`; the three rate limit policies; SdkGen run. Console: the `sessionOptional` skip in the global handler; the Members page with the members table, the Invite dialog and link step, `Members` in `orgNav`; the `/invite` page with all its states; the read only prefilled email in `AuthForm`. Satisfies **AC-1**, **AC-2**, **AC-3**, **AC-5**, **AC-6**, **AC-7**, **AC-8**, **AC-15**, **AC-16**, **AC-17**, **AC-20**, **AC-21**, **AC-22**.
2. Member management. `consoleMembers.update` and `.remove` (leave included) with the org lock, the last owner rule, and the error order; `consoleInvitations.list` and `.revoke`; every AC-11 event with its role values and reasons; the Pending invitations section with Resend and Revoke; the row menu, change role, remove, and leave dialogs, the `deleting` org reasons, and the cache refresh rules. Satisfies **AC-4**, **AC-9**, **AC-10**, **AC-11**, **AC-18**, **AC-19**.
3. Names on keys. `ConsoleUserRef` and `ApiKey.createdBy` resolved in one batch per page and filled on create; the keys table's Created by column. Satisfies **AC-14**, **AC-25**.
4. Install admin and open sign up. `IInstallAdmins`; `ConsoleAccount` on the three account operations; `signupOpen` on `getSetup`; the `/install` page and account menu item; the `AuthForm` sign up mode, the `/sign-up` page, and the sign in page link. Satisfies **AC-12**, **AC-13**, **AC-23**, **AC-24**.
5. Quality and end to end. Document titles, catalog examples, keyboard scripts, axe runs in both themes and densities, integration tests on real Postgres for every scenario above, and the Playwright flow in the `console` compose profile. Satisfies **AC-26**, **AC-27**.

## Consequences

**Positive**:
- Teams work on a self hosted install with no mail server, and the same link flow still works once email arrives (email then only delivers the url).
- One accept path serves new and existing accounts, and every membership change shares one lock, so the owner rule and the single use token hold under races.
- A leaked database dump still exposes no working invite token, and a non member's email is gone at most 37 days after the invite was made (or whenever the org next invites someone after that).
- The API keys table names people, and install admins finally have a screen for sign up mode.

**Negative / tradeoffs**:
- The owner must share the link themselves, and a link lost before sharing costs a Resend (which kills the old link).
- A link is a bearer credential for 7 days; the email match limits a leak to someone who can sign in (or sign up) as that email. Console accounts have no email verification yet, so on an install with open sign up, someone holding a leaked link could sign up with the invited email first. Email verification (a later auth row) closes this.
- Expired invitations are cleaned only when the org next creates an invitation, so an org that never invites again keeps its last expired ones (bounded by the cap) until it is purged.
- A blocked owner still counts as an owner, so an org can be left with no active owner; an install admin row can fix that later.
- `consoleAccount.*` now returns `ConsoleAccount` instead of `User`, a type change in the private console client that the console must follow in one go.
- Platform now depends on an Auth implemented contract at run time, so a Platform unit test needs a fake `IConsoleUserDirectory` (integration tests use the real one).

**Neutral**:
- Five new error codes, a new `FailureKind`, two contract files, three rate limit policies, one migration, and no new settings.
- Invitations change spec 0003's sketch (no `accepted_at`, `revoked_at`, or partial index); 0003 needs a pointer to this spec.

## Follow-up

- [ ] Spec 0003: note that spec 0008 replaces the `platform_invitations` sketch (rows deleted on accept, revoke, replace, and 30 days after expiry; a plain unique index), and that `IUserDirectory`'s console use moved to Platform's `IConsoleUserDirectory` because Auth references Platform. Decide then whether Auth's `IUserDirectory` keeps any consumer or goes.
- [ ] Spec 0007: tick its row 15 follow ups (Teammate names, Members in `orgNav`, the sign up page, the install settings screen and admin flag); the "retry a failed purge on any org" admin power stays open.
- [ ] Spec 0005: tick "Row 15: add Members to `orgNav`".
- [ ] Scope row 9 (transactional email): once the server can send email, deliver the invite url by email as well as showing it once.
- [ ] Scope row 10 (email verification): require a verified console email before accepting an invitation, which closes the open sign up gap in Consequences.
- [ ] Scope row 41 (install admin console): this spec builds its sign up mode switch and `/sign-up` page, so row 41 narrows to retrying failed purges on any org, plus listing, blocking, and deleting console accounts, adding install admins, and repairing orgs with no active owner. Reconcile it with `/scope`.
- [ ] Scope row 15 carries no `GA` tag, but it changes who can reach every project. Consider tagging it `GA` so it also gets `/check review` and `/document`.
