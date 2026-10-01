# 0009. Rationale: transactional email

The decision record behind [index.md](index.md): the problem, the options weighed, why one was chosen, and the smaller calls made while writing the spec.

## Context

Orvano cannot send email. Spec 0004 lets unverified emails sign in because "v0.1 has no email sending", and spec 0008 shows invite links once for the owner to paste into a chat for the same reason. Row 10 (verification, password reset, magic links, email codes) cannot start until something delivers email, and row 9's own Done when asks for three things: SMTP set per project, a template edited in the console, and a test email that arrives.

The forces are specific to a self hosted, multi project server. Most installs are one person who owns every project, so asking each project to set up mail before auth works is friction; yet a project on its own domain must be able to send from it. Project developers are not the install admin: a host they type is a host the server connects to, from inside the Docker network where Postgres and the api's internal port live. Templates are written by project developers but filled with values app users control (their name), and they end up in other people's inboxes. The emails that matter most carry live credentials (reset links, sign in codes, invite links), and spec 0003 promises that a database dump exposes no working token. Spec 0002 puts every event and job in the transaction of the change that caused it, and gives `SecretBox` only to `api` and `worker`.

Later rows lean on whatever this row builds. Row 24 adds SMS, push, and more email providers; row 26 fans workflows out to email with templates and digests. If email is built as a one off inside Auth, or with a template language that can't loop, those rows either move it or build a second one. And because this is infrastructure, not a product feature with its own SDK surface, the risk is building too little (row 10 stalls) or too much (row 24's job done early and badly).

## Options considered

### Option 1: A Messaging module with per project SMTP, install fallback, Liquid templates, and a sealed queue (chosen)

A new `Orvano.Messaging` module. SMTP settings are one row per project, with the reserved `console` project's row serving as the install default. Templates are Liquid (via Fluid), stored only when edited. Callers queue an email inside their own transaction; the api renders it and seals the recipient and content with `SecretBox` in a queue row whose job carries only the row ID; the worker sends through MailKit with a short retry window, then clears the sealed content and keeps a masked log for 30 days.

**Pros**:
- One SMTP setup covers every project, and any project can override it.
- Emails exist only if the change that asked for them commits, and no credential or live link sits in plain text anywhere.
- Liquid is sandboxed by design and can loop, so row 26 reuses the engine.
- Row 24 grows this module instead of replacing it.

**Cons**:
- The most moving parts: a new module, three tables, a job with its own retry schedule, a template validator, and a code editor in the console.
- At least once delivery can send a duplicate after a worker crash.
- The shared install SMTP needs a per project cap to protect its reputation.

### Option 2: Install SMTP from environment variables, templates fixed in code

`ORVANO_SMTP_*` settings validated at startup, one sender for everything, emails built in C# with no editing, sent through the job queue.

**Pros**:
- Smallest build: no console screens, no template engine, no per project storage.
- Fits the existing "validate every `ORVANO_*` setting at startup" rule directly.

**Cons**:
- Fails row 9's Done when outright: no per project SMTP and no template editing.
- Every project sends from the install's domain, and changing SMTP means editing `.env` and restarting.
- Row 10's emails would be unbranded until a later row adds editing anyway.

### Option 3: HTTP email providers (such as SES, Postmark, or Resend) instead of SMTP

Per project provider settings with an API key, sending through each provider's HTTP API, with bounce and delivery webhooks.

**Pros**:
- Real delivery status (delivered, bounced, complained), not just "the server accepted it".
- Faster sends and no SMTP connection handling.

**Cons**:
- One integration per provider, each with its own SDK, errors, and webhooks, which is row 24's work pulled forward.
- Self hosters without an account at a supported provider can't send at all, while almost every mail service (including those providers) speaks SMTP.
- Inbound webhooks need a public route per provider and signature checks per provider.

### Option 4: Send synchronously from the api, no queue

The request that needs an email renders it and talks to the SMTP server before it answers.

**Pros**:
- No queue row, no job, no worker path; the caller learns right away whether it was sent.
- Nothing is ever stored, sealed or not.

**Cons**:
- A slow or down SMTP server stalls sign up and reset requests for seconds, or fails them.
- No retry for a network blip, so emails are lost silently from the user's side.
- An email can be sent even though the transaction that asked for it later rolls back, breaking spec 0002's rule.

## Rationale

Option 1 is the only one that meets the Done when without building row 24 early. Option 2 is simpler but does not deliver per project SMTP or editable templates, so it would be redone before row 10 finished. Option 3 solves delivery tracking, which nothing needs yet, at the cost of locking self hosters into providers; SMTP is the one protocol every provider and every self hoster's mail server shares, and row 24 can add HTTP providers beside it. Option 4 trades the transaction rule and resilience for fewer parts, and auth emails are exactly where a flaky SMTP server must not block a request.

Within Option 1, each engineer choice follows a force from Context. The install fallback exists because most installs are one owner with many projects; storing it as the `console` project's row reuses the fact that console accounts already live in that reserved project, so one table and one code path serve both. Rendering at queue time in the api, not in the worker, means the worker never needs the template engine or `PublicUrl`, and what was previewed is what is sent even if a template changes while an email waits. Sealing the content and passing only an ID in the job keeps spec 0003's dump promise, because `orvano.jobs` payloads are plain JSON. Reading SMTP at send time (not queue time) means fixing a wrong password rescues emails still retrying. The short retry window and the 30 minute stale rule accept losing an email over delivering a dead link, because every auth email has a resend path. The private host block applies only to project SMTP, because project developers are not trusted with the server's network while the install admin already controls the server. Liquid through Fluid was chosen over plain placeholders because Liquid was built for untrusted templates and row 26 needs its loops; the strict variable check at save stops the most likely real bug, a typo that silently empties a reset link.

### Calls made while writing (recommended, not asked)

| Call | Pick | Why | Runner up |
|---|---|---|---|
| Test sends in the log | Not logged; the result shows inline | A test isn't a queued email, and logging unsaved settings' tests muddles `smtp_source` | Log them with a `test` template |
| Stale queue rule | Fail with `email_expired` after 30 minutes | A worker outage must not deliver expired codes; invites have Resend | Send whenever the worker returns |
| Custom retry schedule | `JobRetryException(TimeSpan)` in Core | The default backoff (seconds) can't give a 15 minute window, and webhooks will need the same | A private loop inside the handler |
| Invite email seam | `IConsoleInvitationMailer` owned by Platform, implemented by Messaging | Messaging must reference Platform (access checks), so Platform can't reference Messaging | Messaging listening to `platform.invitation.created` (can't carry the url, which Platform never stores) |
| Project name in templates | Add `Name` to `ProjectInfo` | Messaging can't read Platform's tables; every template needs the name | Make every caller pass it |
| Unknown variable check | Walk the parsed template for names, plus one sample render within limits | A sample render alone misses variables in branches not taken | Render only |
| Template limits | 100,000 steps, 1 MB output, no `include` or `render` | Bounds CPU and memory for any template; no file access | Fluid's defaults |
| Message-ID | `<emailId@public host>`, fixed at queue time | Stable across retries, so some receivers drop a duplicate | Let MimeKit generate one per attempt |
| SMTP and template writes | PUT, full replace | Forms always send every field; safe to repeat | PATCH |
| SMTP failures | Problem details, 502 and 504 with specific codes | Keeps the one error pattern; the console maps codes to messages | 200 with an outcome object |
| Masked recipient | First character, `***`, full domain | Enough to recognize an address, not to recover it | Hash only |
| Test rate limit | 30 per 15 minutes per console user | Room to iterate on a template, too little to probe a network | 10 per minute |
| Connections | One per email, no pooling | Auth volume is low; pooling adds stale connection handling | A pooled client per settings row |
| Invites and the cap | Counted (the `console` project's rows); a capped invite is created with `emailed: false` | With open console sign up, spec 0008's per account limit alone lets every new account send 60 branded emails an hour through the install SMTP (found by the cross check) | Exempt them |
| Stored password reuse | Only for the same host, port, and username | Otherwise a developer who never saw the password could point the host at their own server and capture it (found by the cross check) | Always reuse on `null` |
| DNS answers with mixed addresses | Refuse the host | Skipping the private addresses would still let DNS steer a later connection inward | Dial only the public ones |
| Unreadable sealed data | Fail at once with `email_unreadable` | A removed master key never heals by retrying | Let the job die |
| Stuck `queued` rows | Retention fails any older than 30 minutes | The lease reaper can mark a job `dead` without running the handler, which would leave sealed content for 30 days | Leave them to the 30 day delete |
| Unencoded output | Remove Fluid's `raw` filter, cap every string filter at 1 MB | `raw` would undo the HTML encoding, and `MaxSteps` alone doesn't bound memory | Trust template authors |
| `project.name` at queue time | Passed in by the caller | Reading it through `IProjectDirectory` would take a second pool connection while the caller's transaction is open | Messaging looks it up |
| Writes on a deleting project | 409 `project_not_ready`, reads allowed | Same rule as keys and platforms today | Allow everything |

## Evidence: what already exists

- `server/src/Orvano.Core/Jobs/JobStore.cs`: `Backoff(attempt)` is `2^attempt` seconds with jitter, capped at an hour, and `PermanentJobFailureException` marks a job dead at once. Nothing lets a handler choose its delay, hence `JobRetryException`. `ReapExpiredLeasesAsync` marks a job `dead` once its attempts are used up, without running the handler again, hence AC-17's retention sweep.
- `server/src/Orvano.Core/Secrets/SecretBox.cs`: AES-256-GCM envelope encryption bound to `<table>:<rowId>:<column>`, registered in `api` and `worker` only.
- `server/src/Orvano.Platform/Contracts/Projects.cs`: `ProjectInfo(Id, OrgId, Kind, Status, PurgeFailedAt)` has no name.
- `server/src/Orvano.Auth/Orvano.Auth.csproj` references Platform; Platform references neither Auth nor any other module.
- `server/src/Orvano.Auth/Jobs/AuthJobs.cs`: Auth consumes `platform.project.purged` as `auth.purge_users`; Messaging mirrors it.
- `contract/AGENTS.md`: path parameters are primitive, and a `list` operation must be paged, hence `kind` as a string and `getCatalog`.
- `contract/errors.tsp` has `rate_limited`, `project_not_ready`, `forbidden`, and `invalid_request`, which this row reuses.
- Spec 0008's rationale (Option 2) deferred SMTP here so there would be one email system, and its Follow-up asks this row to email the invite url.
- `.claude/skills/email-best-practices/` (installed during this design): the accessibility rules in AC-13 (`lang` and `dir` in two places, presentational tables, one `h1`, button and code sizes) and the advice against `noreply@` senders.

## Preview frame (decided 2026-09-30, after slice 3)

### Context

Slice 3's verify found that the template preview looks right under the AppHost and wrong behind the gateway. The editor drew the rendered HTML in `<iframe sandbox="" srcdoc>`. A `srcdoc` document inherits its parent's Content Security Policy (the browser's list of what a page may load and run), and `sandbox` changes nothing about that. So the console's policy from spec 0005 (`style-src 'self'` plus one hash, `img-src 'self' data:`) applied inside the email. The browser blocked all 13 inline styles of the default password reset template, showed its hidden preheader, drew the button as a plain link, and would block any remote logo. The AppHost sends no policy, so dev never showed it. The end to end test missed it too: its `securitypolicyviolation` listener sits on the console page, and a frame's violations fire on the frame's own document.

The forces: email HTML is styled almost entirely by inline `style` attributes and `<style>` blocks, so a preview without them misleads. Every console page must keep spec 0005's strict policy (`e2e/headers.spec.ts` asserts no `unsafe-inline`). Templates are written by developers and previewed by owners, so the preview must stay a place where a template can't act against the person viewing it. And the contract rules allow only JSON or empty responses with named model bodies.

### Options considered

**Option A: a static frame page with its own policy, fed by `postMessage` (chosen).** A page in `console/public/frames/`, served by Caddy with a policy written for email, runs in an opaque origin, receives the HTML the editor already has, and draws it in an inner `srcdoc` frame without `allow-scripts`, which inherits the email policy.
- Pros: no API, contract, or SDK change, and still one request per keystroke. The email's scripts are stopped by the sandbox and by the policy, two separate layers. The console's own policy doesn't change. Row 26 can reuse it.
- Cons: a small script runs in the frame page, and its hash must be kept in step with the policy. It's a second policy to maintain, and it adds a path rule to the Caddyfile.

**Option B: the API serves the preview as a document.** The editor posts a form into the frame, and the API renders it and answers `text/html` with its own policy.
- Pros: no script in any frame. The API sets the header, so dev and production match with no extra work.
- Cons: breaks the contract rule (one JSON or empty 2xx, named model bodies), so it needs a rule exception or an endpoint outside the contract. Every keystroke costs two requests, which doubles the cost against `messaging.preview.user`. Form encoded bodies are a second request format on a JSON API.

**Option C: loosen the console's policy.** Add `style-src-attr 'unsafe-inline'`, `style-src-elem 'unsafe-inline'`, and `img-src https:` for every console file.
- Pros: one line, no new files.
- Cons: weakens every console page against style injection and remote image beacons, reverses spec 0005's rule, and fails `headers.spec.ts`. It also doesn't cover what a frame should block anyway (remote stylesheets, frames).

### Rationale

Option A keeps the console's policy whole (the spec 0005 force), keeps the email unable to run script (the force about developers and owners), and doesn't bend the contract (the contract force). Option B's main gain, dev parity, is bought back cheaply with a Vite plugin that reads the same header file. Option C fixes the preview by weakening the one thing spec 0005 promised.

The engineer chose each policy point: remote images load (`https:` and `data:`), since a preview without the logo misleads, accepting that the image host sees the previewing member's IP address. Remote stylesheets and fonts are blocked, since Gmail and Outlook drop them, so the preview shows what most readers see. Links open in a new tab, so an author can check where they go. Dev serves the frame page with the production headers, since a missing policy in dev is exactly how this slipped through.

### Calls made while writing (recommended, not asked)

| Call | Pick | Why | Runner up |
|---|---|---|---|
| Where the email renders | An inner `srcdoc` frame without `allow-scripts`, inside the frame page | The email's script stays blocked by the sandbox even if the policy is ever wrong | `document.write` into the frame page, leaving the policy as the only guard |
| Opening links in a new tab | Parse with `DOMParser`, add `<base target="_blank">`, serialize with the original doctype | The inner frame gets its own opaque origin (sandbox flags are inherited), so the page can't reach into it after load; a parsed copy loads and runs nothing | Rewrite every `href` with string edits |
| Referrer | `no-referrer` on the frame page, inherited by the email | Image hosts and opened links don't learn the console's address | The console's `strict-origin-when-cross-origin` |
| `sandbox` in the policy header too | Yes, the same flags as the outer frame | The page stays sandboxed even if opened on its own, not in a frame | The `iframe` attribute only |
| Message shape | `orvano.email-preview.ready` and `orvano.email-preview.render`, checked on `source` and shape | The page can't check the sender's origin (it is opaque), but it can check the sender is its parent | Accept any message |
| One source for the headers | A `.caddy` file the Caddyfile imports and the Vite plugin reads | One file to edit; the plugin fails loudly if it can't find the policy | The same string in two places, with a test checking they match |
| Hash drift | A Vitest node test comparing the script's hash with the policy | Fails in the unit run, before the slower end to end test | Rely on the end to end test |
| Fonts | Blocked, including `data:` | The engineer chose to block remote fonts; inline `data:` fonts are rare in email and widen the policy for little | `font-src data:` |
| Links other than `http`, `https`, `mailto` | `href` removed in the preview copy, `rel="noopener noreferrer"` on all | `allow-popups-to-escape-sandbox` lifts the sandbox for popups, so a `javascript:` link must never reach it (found by the cross check) | Trust the hash only `script-src` to stop it |
| A template's own referrer settings | Removed or overridden in the preview copy | Inherited `no-referrer` is only a default; `<meta name="referrer">` would undo it (found by the cross check) | Say "no Referer by default" |
| The preview during an error | Keep the last successful HTML mounted and dimmed | `keepPreviousData` drops data on an error, which would reload the frame after every fix (found by the cross check) | Unmount and reload |
| Reposting unchanged HTML | Never: the editor and the page both skip it | Setting `srcdoc` again reloads the frame and fetches the images again | Post on every answer |
| A frame that never loads | A message after 5 seconds | A header regression would otherwise show as a blank white box | Leave it blank |
| Hashed bytes | `.prettierignore` and `eol=lf` for the frame page and header file | `pnpm format` or a Windows checkout would change the hash (found by the cross check) | Compute the hash at build time |
| Caddy override | A nested `handle` for the path | Runs after the outer `header` block, whatever the directive order | A sibling `header @frame` that depends on source order |
