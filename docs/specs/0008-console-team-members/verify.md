# Verify: Console team members & roles · spec 0008 · updated 2026-09-29
_Steps derived from spec 0008 acceptance criteria and its Value sourcing table. `/check verify` runs these; `/test` locks the durable ones._

Start the stack with `docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait` and use the console on `http://localhost:8081`, signed in as the fixture owner (`fixture-admin@example.com`, the install admin).

## UI / manual
- [x] Open the Fixtures org: the sidebar shows Projects, Members, Settings, and Members uses the `Users` icon (not `UsersRound`) → AC-15
- [x] Members page: your row has a "You" badge, a member with no name shows the part of their email before `@`, the tab title reads `Members · Fixtures · Orvano` → AC-15, AC-26
- [x] Invite `grace+1@example.com` as Developer: the dialog switches to "Share this invite link", focus is on the copy button, and it shows "For … as developer" and "Expires <date>" in your time zone → AC-16, AC-17
- [x] Close the link step with Escape, then search the DOM, `localStorage`, `sessionStorage`, and the address bar for the link: it is nowhere → AC-17
- [x] Invite the email of an existing member: "already a member" shows under Email and the dialog stays open → AC-16
- [x] Pending invitations lists the invite with Invited by = your name; Resend opens a new link step; the old link now shows "This invite link isn't valid anymore" → AC-2, AC-18
- [x] Revoke an invitation: the confirm starts on Cancel, and after confirming the row goes and its link answers 404 → AC-4, AC-18
- [x] Sign in as a developer member: Invite is disabled with "Owners only", Pending invitations is not shown, and the network tab shows no `GET …/invitations` → AC-16, AC-18
- [x] Open an invite link in a private window (keep only its `#token` part on `:8081`): the fragment leaves the address bar, the page reads "<inviter> invited you to join Fixtures as developer", and it never redirects to sign in → AC-20, AC-21
- [x] In that window, create the account on the Create account tab (email read only): you land on the Fixtures org with "You joined Fixtures" → AC-7, AC-22
- [x] Open a fresh invite while signed in as a different email: the page names both emails; Sign out keeps you on the page in the signed out state with the same invite → AC-21
- [x] Open a used link, a revoked link, and an expired one (set `expires_at` in the past with psql): each shows its own message (404, 404, 410) → AC-5, AC-20
- [x] Change the new member to Viewer from the row menu: Save stays disabled until the role differs; as that viewer, "Create key" on the scenarios project is disabled with "Developers and owners only" → AC-9, AC-19
- [x] As the only owner of an org, demote yourself: the warning "You'll lose owner rights in this org" shows, and Save answers with the last owner message inside the dialog → AC-9, AC-19
- [x] Remove the member: the confirm names them and says their API keys keep working; their next visit to the org shows "Org not found" → AC-10, AC-19
- [x] As a member, Leave org: a toast confirms and you land on your next org (or `/orgs` when none remain) → AC-10, AC-19
- [x] Delete an org, then open its Members page: Invite, Change role, Remove, Leave, Resend, and Revoke are off with "Restore the org first" → AC-3, AC-19
- [x] API keys page: Created by shows "You", else the creator's name, else their email, and "Deleted account" after that account is deleted → AC-14, AC-25
- [x] Account menu as the install admin shows "Install settings" above Sign out; `/install` switches console sign up to "Anyone can sign up" with its warning; as a non admin, `/install` shows the in shell not found screen → AC-24
- [x] With sign up open, `/sign-in` shows a "Create account" link and `/sign-up` creates an account and lands in the shell; with invite only, `/sign-up` shows the invitation message → AC-13, AC-23
- [x] Open `/invite#<first token>`, then paste a second invite link into the same tab: the page previews the second invite (its role and email) without a reload, and the fragment leaves the address bar again → AC-20
- [x] Open Change role on your own row: it reads "Your role in this org. It takes effect on the next thing you do." → AC-19

## Commands
- [x] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Platform.MembersAndInvitationsTests"` → 15 passed → AC-1 to AC-14
- [x] `dotnet test --project server/tests/Orvano.Server.Tests -- --filter-class "Orvano.Server.Tests.Platform.InvitationRulesTests" --filter-class "Orvano.Server.Tests.Platform.PlatformLifecycleTests"` → all pass, including the org purge with a pending invitation → AC-1, AC-2, AC-5
- [x] `pnpm --filter @orvano/console test` → all pass, with the members, invite, sign up, install, and keyboard script suites running axe in both themes → AC-15 to AC-26
- [x] `pnpm --filter @orvano/console test:e2e` against the console profile → `members.spec.ts` passes, and `members.spec.ts design-system.spec.ts --repeat-each=5` passes 35 of 35 → AC-27
- [x] EF drift check against an empty bootstrapped Postgres → "EF model matches the database" → data model

## Value sourcing
- [x] Token: the create response's `url` ends in `/invite#` plus 43 base64url characters, and `platform_invitations.token_hash` is 32 bytes equal to `sha256(token)`; the token appears in no `orvano.events` row and no api log line
- [x] `url`: built from `ORVANO_PUBLIC_URL` (start the api with a different public URL and check the link's origin follows it)
- [x] `expires_at`: the database clock plus 7 days (compare with `now()` in psql, not the browser clock)
- [x] Replace and cleanup: inviting `GRACE@x.com` after `grace@x.com` leaves one row; a row expired 31 days ago goes on the next create, one expired 29 days ago stays
- [x] Already a member: matched by `lower(email)` through Auth, so `ADA@x.com` is refused when `ada@x.com` is a member
- [x] Cap: 100 rows after the replace and the cleanup; the 101st answers `invitation_limit`, while a replace at 100 still works
- [x] List status: an invitation whose `expires_at` equals `now()` lists as `expired` (the database clock decides, at or before)
- [x] `invitedBy` and preview `invitedByName`: delete the inviter's console account and see null / "Deleted account"; with no name, the email shows
- [x] Accept email match: `lower()` on both sides, so an account `Grace@X.com` accepts an invite for `grace@x.com`
- [x] Accept role: the membership gets the invitation's role; accepting as an existing member keeps their role and answers `alreadyMember: true`
- [x] Error pick when nothing was deleted: an expired invite for another email answers 410, not 403; a live one for another email answers 403 and the row stays
- [x] Invited sign up lands on the preview's `orgId`
- [x] Members `joinedAt` equals `platform_memberships.created_at`; name, email, and status come from the console account (block the account and see "Blocked")
- [x] "You" badge and Leave follow `ConsoleAccount.id`; owner actions follow `Org.role`; "Restore the org first" follows `Org.status = deleting`
- [x] `isInstallAdmin` follows `platform_install_admins`; `signupOpen` follows `platform_install_settings.console_signup`
- [x] `ApiKey.createdBy` resolves every creator on a page in one batch, and null once the creator's account is gone
- [x] Event payloads carry IDs, the actor, role values, reasons, and field names only: `SELECT payload FROM orvano.events WHERE type LIKE 'platform.invitation.%' OR type LIKE 'platform.member.%'` shows no email, token, hash, or url

## Acceptance criteria coverage
- AC-1 to AC-3: server tests (create, replace, race, cap, order) and the invite steps above
- AC-4, AC-5, AC-6, AC-7: server tests (list, revoke, preview, accept, invited sign up, races, format) and the invite page steps
- AC-8 to AC-11: server tests (members list, role change, remove, leave, last owner, events)
- AC-12 to AC-14: server test for the account flag, `signupOpen`, and `createdBy`
- AC-15 to AC-19: Members page browser tests and the UI steps
- AC-20 to AC-22: invite page browser tests and the UI steps
- AC-23, AC-24, AC-25: sign up, install, and keys browser tests and the UI steps
- AC-26: catalog keyboard scripts with axe, page axe runs in both themes and densities, titles
- AC-27: `console/e2e/members.spec.ts`
