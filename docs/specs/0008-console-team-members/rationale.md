# 0008. Rationale: console team members, invitations, and roles

The decision record behind [index.md](index.md). `/develop` builds from `index.md` and skips this file.

## Context

Scope row 15 promises that an org owner can invite teammates by email, give each a role, and remove them, with access ending at once. Spec 0003 already fixed the parts that hang off the database: the three roles and their permission matrix, the last owner rule and its org row lock, and a sketch of `platform_invitations` with a hashed, single use token that expires after 7 days. The invite only sign up gate from spec 0003 AC-7 also waits on this row: until invitations exist, an install in `invite` mode can never gain a second console account.

Four things were left open. The server has no way to send email (the messaging product is a later row), so the "by email" in the scope row has no delivery path. Spec 0003 only accepts an invitation during sign up, so a teammate who already has a console account on the install has no way to join another org. Spec 0007 left the Members screen, the invite link page, a sign up page, and an install settings screen as follow ups with no design. And none of the contract operations or error codes for members and invitations exist.

One structural fact constrains the server design: `Orvano.Auth` references `Orvano.Platform` (Auth calls Platform's sign up hooks inside its transaction). Spec 0003 put the user lookup the members list needs (`IUserDirectory`) in Auth, which Platform cannot reference without a cycle.

The forces are a self hosted product that must work on a server with no mail setup, access control that must hold under concurrent changes, personal data (emails of people who never join) that should not linger, and the Tracer Bullet rule that the row ships end to end in the console, not only the API.

## Options considered

### Option 1: Copy once invite links with a public invite page

The owner creates an invitation and the console shows its link once. The teammate opens `/invite`, which reads the invitation without a session and lets them join with an existing account (email must match) or create one. Accepted, revoked, and replaced invitations are deleted. Email delivery comes later from the messaging row.

**Pros**:
- Works on every install today, with no mail server or new settings.
- Keeps spec 0003's promise that a database dump exposes no working token, because the token is stored only as a hash.
- One accept path covers new and existing accounts.

**Cons**:
- The owner must pass the link on by hand, and a link lost before sharing costs a Resend.
- A link is a bearer credential until it expires; the email match limits but does not remove that risk while emails are unverified.

### Option 2: Add SMTP settings and send invite emails now

New `ORVANO_SMTP_*` settings and a mail sender in this row, with a copy link fallback.

**Pros**:
- Matches the scope row's words ("invite by email") literally, and owners never handle links.

**Cons**:
- Pulls email infrastructure (templates, retries, bounces, a job queue consumer) forward from the messaging product, where it will be designed properly for app users as well.
- Every install must configure a mail server before teams work, or fall back to the link anyway.

### Option 3: Retrievable links (token stored encrypted)

Like Option 1, but pending invitations keep a copy button, so the link can be fetched again at any time.

**Pros**:
- No need to resend a lost link.

**Cons**:
- Stores a working credential (envelope encrypted) in the database, breaking spec 0003's "no working token in a dump" guarantee for a small convenience that Resend already covers.

### Option 4: Add existing accounts directly, invite only new ones

An owner types an email; if an account exists it becomes a member at once, otherwise an invitation is created for sign up.

**Pros**:
- Fewer steps for existing accounts.

**Cons**:
- Tells any owner whether an email has an account on the install (an account existence oracle), and adds people to orgs without their consent.
- Two different flows to build, test, and explain.

## Rationale

Option 1 is chosen because it is the only one that works on every self hosted install today without weakening a guarantee already made. Email delivery is a real need, but it belongs to the messaging product, which has to solve delivery for app users anyway; building a one off mail path here would mean two email systems. Links lose nothing when email arrives, since email then only carries the same url. Storing tokens so links can be fetched again (Option 3) would trade spec 0003's "a dump exposes no working token" for a convenience that Resend already gives. Adding accounts directly (Option 4) leaks which emails have accounts and skips consent.

The smaller calls follow the same forces. The token sits after `#` in the url, so it never reaches the server, the gateway's access logs, or a Referer header, and preview and accept take it in a POST body for the same reason. Binding an invitation to its email, ignoring case, means a forwarded or leaked link can't admit a stranger with a different account. That leaves the unverified email gap named in Consequences, which the auth email verification row closes. Deleting rows on accept, revoke, and replace keeps a non member's email only as long as the invitation is useful, lets the outbox events carry the history for the audit log, and turns spec 0003's partial unique index into a plain one, so an expired invitation never blocks a new one. Expired rows are swept 30 days after expiry by the org's next invitation create, one extra statement under a lock the create already holds, instead of a recurring job this row would have to own. Replacing instead of refusing a second invite makes "invite again" and "resend" the same safe action. Every membership and invitation change takes the org row lock, which already guards the owner rule, so the cap, the one invitation per email rule, and racing accepts need no new locking scheme.

Some calls were made in the design without a question, each with its runner up. `IConsoleUserDirectory` is owned by Platform and implemented by Auth, rather than moving the lookup into `Orvano.Core` (the runner up), because it keeps the lookup's shape in the module that uses it and follows the pattern the sign up hooks already use. The invitation `status` is computed by the database clock, not the browser's, so an owner with a wrong clock still sees the truth (runner up: the client compares `expiresAt`). The accept operation returns `alreadyMember` rather than a separate status code, so a harmless second click stays a success. The invite page clears the token from the address bar and keeps it only in memory, the way `/setup` already does, accepting that a reload means opening the link again (runner up: `sessionStorage`, which would keep a credential in storage). The page probes the session with a query that opts out of the global "no session, go to sign in" handler, because that redirect would drop the token (runner up: a new sessionless "who am I" call, one more operation for the same answer). The invitation is consumed inside `AdmitAsync` rather than `OnCreatedAsync`, because only `AdmitAsync` can refuse a sign up. After signing in on the invite page you still press Join rather than accepting automatically, so the page has one path to membership and you always see which org you are joining.

## Evidence: what already exists

- `server/src/Orvano.Platform/Application/ConsoleAccounts.cs`: `AdmitAsync` refuses every invite token today ("Invitations arrive with row 15"), and `OnCreatedAsync` already adds an invite's membership when `SignupAdmission.Admitted` carries `InviteOrgId` and `InviteRole`.
- `contract/auth/console-account.tsp`: `CreateConsoleAccountRequest.inviteToken` already exists; `consoleAccount.get` returns spec 0004's `User`.
- `contract/platform/install.tsp`: `consoleInstall.getSettings` and `.updateSettings` exist for install admins; `getSetup` returns only `setupRequired`.
- `console/src/shell/nav.ts`: `orgNav` holds Projects and Settings; spec 0005 and spec 0007 both left "add Members" to row 15.
- `server/src/Orvano.Auth/Orvano.Auth.csproj` references `Orvano.Platform`; `Orvano.Platform.csproj` references only `Orvano.Contract` and `Orvano.Core`.
