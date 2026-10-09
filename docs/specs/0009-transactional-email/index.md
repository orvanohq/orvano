# 0009. Transactional email: SMTP per project, editable templates, and a send queue

**Date**: 2026-09-29
**Updated**: 2026-10-08 (spec 0013: the `security_alert` template kind and `IEmailQueue.QueueSecurityAlertAsync`, with no `ActionUrl` or `Code`); 2026-10-02 (the `ActionUrl` rule becomes a scheme deny list and `IEmailQueue` gains `CheckAvailabilityAsync`, spec 0010)
**Status**: Accepted

## Summary

Orvano learns to send email. The install admin sets one SMTP server (the mail server that relays email) for the whole install, and any project can set its own, which wins. Emails are written in Liquid templates (a safe template language with `{{ variables }}`) that you edit and preview in the console, then queued inside the change that caused them and sent by the worker with retries. This row sends console invites by email and ships the four auth templates ready for row 10 (verify, reset, magic link, email code), so row 10 only has to trigger them.

## Requirements

**User stories**:
- As a project owner or developer, I want to point my project at my own SMTP server and send a test email, so that I know auth emails will arrive from my domain before any user needs one.
- As a project owner or developer, I want to edit, preview, and test the auth email templates in the console, so that they carry my product's words without a deploy.
- As an install admin, I want to set one SMTP server for the whole install, so that every project and the console can send email without each one being set up.
- As an org owner, I want an invite to also arrive by email, so that I don't have to paste the link into a chat.
- As any project member, I want a log of recent emails with their status, so that I can answer "did the reset email go out?" without reading server logs.
- As a later module (row 10's auth flows, row 24's messaging), I want one call that queues a rendered email in my own transaction, so that an email exists only if my change commits.

**Acceptance criteria** (the contract):

*SMTP settings*

- **AC-1**: On the project's Email > Settings tab an owner or developer fills Host, Port, Security, Username, Password, From email, From name, and Reply to, and Save calls `consoleSmtp.update`. The row is stored in `messaging_smtp_settings` with the password sealed by `SecretBox` (associated data `messaging_smtp_settings:<project_id>:password_ciphertext`). No response, log, event, or problem ever carries the password; `consoleSmtp.get` returns `hasPassword: true` instead. On update and on test, `password: null` means "use the stored password" only when a password is stored and `host`, `port`, and `username` all equal the stored row's values; otherwise a set `username` with `password: null` answers 400 `invalid_request` (`password: Enter the password again when you change the host, port, or username`), so a stored password can never be sent to a different server. `username: null` clears the password with it.
- **AC-2**: `consoleSmtp.update` and `consoleSmtp.test` answer 400 `invalid_request` when: host is empty, longer than 253 characters, or neither a host name (labels of letters, digits, and hyphens, 1 to 63 characters each, separated by dots, no trailing dot; international names entered as punycode) nor an IPv4 or IPv6 literal (IPv6 without brackets); port is outside 1 to 65535; From email or Reply to fails Messaging's own copy of the email rule (spec 0004's rule, copied into `Domain/` as Platform copied it) or is longer than 320 characters (the address alone); From name is longer than 128 or contains a line break; `security` is `none` while `username` is set ("A username needs STARTTLS or TLS"); AC-1's password rule. Every field error's `detail` starts with the input's camelCase name and a colon (`host: ...`), and the console shows the rest under that field.
- **AC-3**: For project SMTP (never the install's), only global unicast addresses are allowed. An IP literal outside global unicast, or a name that resolves to any address outside it (even alongside public ones), answers 400 `smtp_host_not_allowed` on update and on test. Outside global unicast means loopback, private (10/8, 172.16/12, 192.168/16), shared (100.64/10), link local (169.254/16, fe80::/10), unique local (fc00::/7), unspecified, multicast, broadcast, `0.0.0.0/8`, documentation (192.0.2/24, 198.51.100/24, 203.0.113/24, 2001:db8::/32), benchmarking (198.18/15), reserved (240/4), `64:ff9b::/96`, `2002::/16`, `2001::/32`, `::/96`, and IPv4 addresses mapped into IPv6 whose IPv4 part is any of these. A name that doesn't resolve yet can be saved (DNS is often set up later); sending then fails with `smtp_unreachable`. The lookup on update takes at most 5 seconds. The check runs again on every connection (test and worker): the connector resolves the name, refuses if any address is outside global unicast, then dials the addresses in the order DNS returned them, at most 10 seconds each, within the operation's total budget (one linked cancellation for the whole send), and hands the connected socket to MailKit, so a DNS answer that changes after the check can't reach an internal service. `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=true` turns this rule off.
- **AC-4**: `consoleSmtp.get` returns `source`: `project` when the project has a row, else `install` when the reserved `console` project has one, else `none`. With `project` it returns the settings (no password). Whenever the `console` row exists, whatever the source, it also returns `installSender` (the install's From email and From name), never its host, port, or username; otherwise `installSender` is null. The Settings tab shows, by source: the form filled with the project's settings; "Using this server's email settings, sending as <installSender>" above an empty form; or "No email server is set up. Auth emails can't be sent until you add one here or the install admin adds one for the whole server." above an empty form.
- **AC-5**: "Stop using these settings" (owners and developers, shown only with `source: project`) opens a confirm that says what happens next, chosen by `installSender`: "Emails will use this server's settings" when it is set, "This project won't be able to send email" when it is null. Focus starts on Cancel. Confirm calls `consoleSmtp.delete` (204, also when nothing is stored), and the tab shows the new source.
- **AC-6**: "Send test email" calls `consoleSmtp.test` with the form's current values, saved or not (with AC-1's password rule). The api connects right away and waits at most 20 seconds in total, sends a fixed test email ("Test email from Orvano" with the project name) to the caller's own console email, stores nothing, and returns `sentTo`. The form shows "Sent to <sentTo>. Check your inbox." Failures answer problem details that the form shows inline, by the *SMTP outcomes* table: 502 `smtp_unreachable`, 502 `smtp_tls_failed`, 502 `smtp_auth_failed`, 502 `smtp_rejected` (`detail` carries the SMTP reply code and text, cut to 200 characters), 504 `smtp_timeout`. A test never retries, so a 4xx reply is also 502 `smtp_rejected` (or `smtp_auth_failed` for AUTH). Certificates are always validated; there is no option to skip that.
- **AC-7**: The install admin sees an "Email server" card on `/install` with the same form, test button, and delete (its confirm says "Projects without their own settings won't be able to send email"). It calls `consoleInstall.getSmtp`, `.updateSmtp`, `.deleteSmtp`, and `.testSmtp`, which read and write the `console` row, with AC-1's password rule. AC-3's private host rule does not apply to it. Anyone else gets 403 `forbidden`, as `consoleInstall.updateSettings` answers a non admin.

*Templates*

- **AC-8**: The Templates tab lists the four templates, Email verification (`verification`), Password reset (`recovery`), Magic link (`magic_link`), and Email code (`email_code`), each with a one line description, a Default or Custom badge, and, when custom, when it was last edited.
- **AC-9**: Opening a template shows a Subject input, an HTML editor, an optional Text editor (placeholder "Leave empty to generate it from the HTML"), and the list of that template's variables with their descriptions. 500 ms after typing stops, the editor calls `consoleEmailTemplates.preview` with the unsaved content and shows the rendered subject, the HTML in the preview frame (AC-31), and the text part (derived when blank). Save calls `.update`. "Reset to default" opens a confirm (focus on Cancel) and calls `.reset`, then shows the default content with the Default badge. Leaving with unsaved changes asks first. Templates are last write wins: two people saving the same template keep the later one. A viewer sees the same page with read only editors, the variable list, and no preview, Save, Reset, or Send test.
- **AC-10**: `update`, `preview`, and `test` answer 422 `template_invalid` with `detail` giving the part, line, and problem (for example "html: line 4: unknown variable action_ur") when: the subject, HTML, or text fails Liquid parsing; it names a variable or a property path that is not in that template's list (`user.emial` fails as surely as `action_ur`; names created in the template by `assign`, `capture`, or `for`, and Liquid's own `forloop` and `tablerowloop` inside their blocks, are allowed); it uses a filter that is not in the allowed set (Liquid's standard filters minus `raw`); it uses `include` or `render`; rendering with the sample values takes more than 100,000 steps, or any value or the output grows past 1 MB. A subject that is empty or longer than 255 characters, or an HTML or text part longer than 100 KB, answers 400 `invalid_request`.
- **AC-11**: Rendering HTML encodes every `{{ }}` output in the HTML part, and only there (the subject and text parts are not encoded); the `raw` filter is removed, so nothing can skip the encoding. Control characters in a rendered subject become spaces, and a rendered subject longer than 255 characters is cut to 255. Fluid (the Liquid engine) runs with no file access, only the allowed filters, access only to the variables given, a limit of 100,000 steps, and a 1 MB limit on the output and on every string a filter returns (`append`, `prepend`, `replace`, `join`, and the rest are wrapped with a size check, since doubling a string in a loop would otherwise reach gigabytes within 30 steps), plus Fluid's own memory or time limits where it offers them, everywhere it runs.
- **AC-12**: "Send test" in the editor calls `consoleEmailTemplates.test` with the unsaved content: it renders with the sample values and sends through the project's effective SMTP (AC-4) to the caller's console email, with AC-6's wait, result, and errors. With `source: none` it answers 409 `email_not_configured`.
- **AC-13**: The four default templates follow the email accessibility rules: `lang="en" dir="ltr"` on `<html>` and on the direct child of `<body>`, a `<title>`, one `<h1>`, layout tables with `role="presentation"`, a single column, body text of at least 16 px, a button at least 44 px tall linking to `action_url`, the code in `email_code` at 28 px in a monospace font, preheader text that says when it expires, a subject naming the project (for example "Reset your password for {{ project.name }}"), and the line "If you didn't ask for this, you can ignore this email." Each also has a hand written text part. Spec 0013 (AC-31) adds the template kind `security_alert`: an editable default with no button and no code, filled with `alert`, `occurred_at`, and `project.name`, worded through a Liquid `case` on `alert`, and following these rules apart from the button.

*Queue and delivery*

- **AC-14**: `IEmailQueue.QueueAuthEmailAsync` (in `Orvano.Messaging.Contracts`) runs in the caller's transaction and, in order: validates the request (`To` passes Messaging's email rule, has no line break, and is at most 320 characters; `ActionUrl` is an absolute URL with no user info whose scheme is not `javascript`, `data`, `vbscript`, `file`, `blob`, or `about` (loosened by [spec 0010](../0010-email-verification-recovery-passwordless/index.md) AC-31: the link now lands on the app's own redirect URL, which Auth checks against the project's platforms before it queues, so Auth is the real gate); `Code` is 4 to 12 letters or digits; anything else throws `ArgumentException`, a caller bug); resolves the project's effective SMTP and returns `NotConfigured`, writing nothing, when there is none; applies AC-19's cap; renders the project's custom template for that kind (or the default) with the given values, including the caller supplied `ProjectName`; takes a new ID from `Guid.CreateVersion7()` (the sealing needs it before the insert); builds a `Message-ID` of `<emailId@host of ORVANO_PUBLIC_URL>`; inserts a `messaging_emails` row (`status: queued`, `recipient_masked`, `content_ciphertext` sealing the full recipient, subject, HTML, text, and Message-ID); and enqueues job `messaging.email.send` on queue `messaging` with `ProjectId` set, payload `{"emailId":"<id>"}`, and 6 attempts. It returns `Queued(emailId)`. If the caller rolls back, neither the row nor the job exists. Spec 0013 (AC-31) adds `QueueSecurityAlertAsync(string projectId, string to, string projectName, SecurityAlertKind alert, DateTimeOffset occurredAt, CancellationToken)` for the `security_alert` kind, which runs the same steps in the caller's transaction (validation, SMTP resolution, AC-19's cap, render, insert, job) but takes no `ActionUrl` or `Code`.
- **AC-15**: The worker's `messaging.email.send` handler first loads the row; if it is missing (retention or purge ran) or already final, the job completes and does nothing. Otherwise, on every attempt, it sets `attempts` to the job's attempt count, reads the project's effective SMTP at that moment (so a fixed password rescues emails still retrying), applies AC-3 for project SMTP, and sends one multipart email (text and HTML) with `From: <From name> <From email>`, `Reply-To` when set, the stored `Message-ID`, and `Auto-Submitted: auto-generated`. On success, one conditional update (`WHERE status = 'queued'`) sets `status: sent`, `smtp_source`, `completed_at`, and `content_ciphertext = NULL`, and writes `messaging.email.sent` in the same transaction. If unsealing the content or the SMTP password throws `SecretBoxException` (a master key removed, or tampered data), the email fails at once with `email_unreadable`.
- **AC-16**: Each attempt's outcome, retry or fail now, and its code come from the *SMTP outcomes* table. A retry waits 30 seconds, then 1, 2, 4, and 8 minutes (each attempt has a 30 second budget). A host that is not allowed, no effective SMTP, or a project that is not `active` also fails at once. The 6th failed attempt fails with that attempt's code. Failing sets `status: failed`, `error_code`, `completed_at`, and `content_ciphertext = NULL` with the same conditional update, and writes `messaging.email.failed`. Once a row is final, its job ends `succeeded`; a job goes `dead` only if recording the outcome itself keeps failing.
- **AC-17**: An attempt that starts more than 30 minutes after the row's `created_at` sends nothing and fails with `email_expired` (a worker that was down must not deliver stale codes and links). The hourly retention schedule also fails every `queued` row older than 30 minutes the same way, so a job that the lease reaper marked `dead` without running the handler never leaves a row queued with sealed content.
- **AC-18**: If a custom template fails to render at queue time (the step or size limit with real values), or renders an empty subject, the default template for that kind is used instead, and a warning log names the email ID and the template kind.
- **AC-19**: When the project has no SMTP row of its own (so it would use the install's), queueing counts that project's `messaging_emails` rows created in the last hour. At or above `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` it returns `RateLimited(retryAfter)` and writes nothing, where `retryAfter` is when the row at offset `count − limit` in `created_at` order (the one whose expiry frees a slot) turns an hour old. The reserved `console` project counts too (its rows are invite emails). The cap is soft: two requests at the same moment can both pass (no lock). A project with its own SMTP has no cap.

*Log*

- **AC-20**: The Log tab, visible to every project member, calls `consoleEmails.list` (keyset paged on `(created_at, id)`, newest first, `limit` 1 to 100, default 25) and shows a table of Template, To (the masked recipient), Status (Queued, Sent, Failed badges), Attempts, Reason (the message for `errorCode`, table in *Screens*), Created, and Completed (both in the viewer's local time). The empty state reads "No emails in the last 30 days". An hourly retention schedule deletes rows whose `created_at` is more than 30 days old.
- **AC-21**: `/install` also shows the console's own email log (`consoleInstall.listEmails`, the `console` project's rows, same table) to the install admin only.
- **AC-22**: The masked recipient keeps the first character of the local part, then `***`, then `@` and the whole domain (`grace@example.com` becomes `g***@example.com`). It is computed once, at queue time.

*Console invites*

- **AC-23**: When the install has SMTP, `consoleInvitations.create` (and Resend, which calls it) also queues the fixed invitation email to the invited address in the same transaction, through Platform's `IConsoleInvitationMailer`: subject "<inviter> invited you to join <org> on Orvano", the org name, the inviter's name (else their email), the role, an "Accept invite" button linking to the invite url, and when it expires (invariant culture, `MMM d, yyyy, HH:mm 'UTC'`). It has a hand written text part and follows AC-13's accessibility rules. The response's new `emailed` field is `true`, and the link step shows "We're sending the invite to <email>." above spec 0008's unchanged link step. With no install SMTP, or when AC-19's cap refuses, `emailed` is `false`, nothing is queued, the invitation is still created, and the dialog is exactly spec 0008's. Any other mailer error (a `SecretBoxException`, a failed insert) propagates and rolls the invitation back. An email already queued for an invitation that is then replaced or revoked is still sent with its now dead link (accepted). The url exists only inside `content_ciphertext`, which is cleared once the email is final.

*Security, data, and operations*

- **AC-24**: Permissions for project operations, checked with `IConsoleAccess.GetProjectRoleAsync`: any member reads `consoleSmtp.get`, `consoleEmailTemplates.getCatalog` and `.get`, and `consoleEmails.list`; owners and developers also update, delete, reset, preview, and test; a viewer gets 403 `forbidden` on those, and the console shows them disabled with "Developers and owners only". Checks run in this order, and the first failure answers: 404 `project_not_found` (not a member, or no such project), 404 `not_found` (unknown template `kind`), 403 `forbidden` (role), 429 `rate_limited` (AC-25), 400 `invalid_request` and 422 `template_invalid` (the body), 409 `project_not_ready` (writes and tests on a project that is not `active`; reads and preview still work), 409 `email_not_configured`, then the SMTP outcome.
- **AC-25**: Every test send (`consoleSmtp.test`, `consoleEmailTemplates.test`, `consoleInstall.testSmtp`) takes the new rate limit `messaging.test.user`, 30 per 15 minutes per console user, and every preview takes `messaging.preview.user`, 300 per 5 minutes per console user. Each counts every attempt that passed the role check (a 403 or 404 counts nothing); over it answers 429 `rate_limited` with `Retry-After`.
- **AC-26**: No secret or personal data leaks. `orvano.jobs` payloads carry only the email ID. Events (`messaging.smtp.updated`, `messaging.smtp.deleted`, `messaging.template.updated`, `messaging.template.reset`, `messaging.email.sent`, `messaging.email.failed`) carry the project ID, the email ID or template kind, the error code, the changed field names, and the actor, never an address, host, username, password, subject, or content. Logs carry the email ID, project ID, error code, and SMTP reply code, never those values either. A final `messaging_emails` row has `content_ciphertext` null.
- **AC-27**: Messaging's consumer `messaging.purge_project` of Platform's `platform.project.purged` event (the project ID read from the event, as Auth's `auth.purge_users` does; it only enqueues, no IO) queues `messaging.project.purge` on queue `messaging` with `ProjectId` set, which deletes the project's rows from all three tables. The job is idempotent.
- **AC-28**: Every role that loads modules (`api`, `worker`, and `realtime`) validates `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS` (`true` or `false`, default `false`) and `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` (an integer from 1 to 1,000,000, default 200) at startup, in `MessagingModule.ConfigureServices`, and refuses to start on a bad value. An unset or empty value means the default, since Compose passes `${VAR:-}` as an empty string. `migrate` never loads modules or sends email, so it neither reads nor checks either setting.
- **AC-29**: The Aspire AppHost starts Mailpit (a local mail catcher) and gives `api` and `worker` `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=true`. `tests/scenarios/compose.yml` adds a `mailpit` service (SMTP on `mailpit:1025`, its API published on `localhost:8025`) with the same setting for the server, and a new fixture in `ORVANO_TEST_FIXTURES` seeds the install SMTP as host `mailpit`, port 1025, `security: none`, no username, From `orvano@scenarios.test`. Integration tests run Mailpit through Testcontainers and read arrived mail through its API.
- **AC-30**: Every new screen meets WCAG AA (axe in both themes and densities). The CodeMirror editors have visible labels and can be left with Escape then Tab, the preview's outer frame has the title "Email preview" and its inner frame "Email content", and every form error is announced.

*Preview frame*

- **AC-31**: The preview HTML renders in two nested frames, so the email never runs script and never sees the console. The outer `<iframe title="Email preview">` loads `/frames/email-preview.html` (a static page in `console/public/frames/`) with `sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox"` and no `allow-same-origin`, so the page runs in an opaque origin (an origin equal to nothing, so it can't read the console's cookies, storage, or page). The page is static: `<html lang="en">`, a `<title>`, a white background with `color-scheme: light` (a dark console never tints the email), and the inner `<iframe title="Email content" sandbox="allow-popups allow-popups-to-escape-sandbox">` already in its markup, filling the page. When it loads, the page posts `{ type: "orvano.email-preview.ready" }` to its parent. The editor answers with `{ type: "orvano.email-preview.render", html }` (AC-33). The page ignores any message whose `source` is not `window.parent`, whose `data` is not an object with exactly that `type` and a string `html` (axe and other tools post their own messages), or whose `html` equals the last one it drew. It parses the HTML with `DOMParser` (an inert copy: nothing runs or loads), then on that copy: puts `<base target="_blank">` first in `<head>`; removes every `href` on `a` and `area` whose scheme is not `http`, `https`, or `mailto` (relative ones included), and sets `rel="noopener noreferrer"` on each; removes every `<meta name="referrer">`; sets `referrerpolicy="no-referrer"` on every `img`. It serializes the copy back with the original doctype (name, public ID, and system ID; none if the email had none, so the rendering mode stays the same) and sets it as the inner frame's `srcdoc`. The inner frame never gets `allow-scripts`, so the email's scripts, event handler attributes, and `javascript:` links never run. An `http`, `https`, or `mailto` link opens a new, unsandboxed tab with no opener; any other link does nothing. These changes exist only in the preview: the saved, tested, and sent HTML is untouched.
- **AC-32**: The gateway serves `/frames/email-preview.html` with its own headers in place of the console's: `Content-Security-Policy: default-src 'none'; script-src 'sha256-<the page's one inline script>'; style-src 'unsafe-inline'; img-src https: data:; base-uri 'none'; form-action 'none'; frame-ancestors 'self'; sandbox allow-scripts allow-popups allow-popups-to-escape-sandbox`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`, the console's `Permissions-Policy`, `Cross-Origin-Opener-Policy: same-origin`, and `Cache-Control: no-cache`. The inner `srcdoc` frame inherits this policy, so inline `style` attributes and `<style>` blocks apply, `https:` and `data:` images load with no `Referer` (AC-31 strips a template's own referrer settings), and remote stylesheets, web fonts, `http:` images, frames, and every other fetch are blocked. Every other console file keeps spec 0005's policy unchanged. The headers live in one file, `deploy/gateway/email-preview-headers.caddy`, which lists every one of them; the Caddyfile imports it for that one path, and the console's Vite dev and preview servers read it, so `pnpm dev`, `vite preview`, and the AppHost serve the page with exactly the production headers. A differently cased path (`/Frames/…`) is not the frame page and gets the console's headers. Behind the gateway, each default template's preview shows its styles (the preheader hidden, the button with its background and at least 44 px tall) and no frame reports a Content Security Policy violation.
- **AC-33**: The editor's side of the frame. The HTML tab stays mounted while the Subject or Text tab shows (no reload, no second image fetch). Before the first successful preview the HTML view shows the Skeleton; after it, the frame stays mounted through later requests, a 422, or a failure, showing the last successful HTML dimmed as today, so fixing an error never reloads the frame. The editor accepts `ready` only when `event.source` is the outer frame's `contentWindow` and the shape matches, and answers every `ready` (a reload included) by posting the latest successful HTML to that `contentWindow` with target origin `"*"`. After that it posts only when the HTML string changes (a change to the subject alone posts nothing). The listener is removed on unmount. If no `ready` arrives within 5 seconds of the frame mounting, the HTML view shows a `FormAlert`: "Couldn't load the preview. Reload the page to try again." The Subject and Text views keep working.

## Decision

**Chosen option**: Option 1: a new `Orvano.Messaging` module that stores SMTP per project with the install's as the fallback, renders Liquid templates with Fluid, and seals each email into an encrypted queue row that the worker sends through MailKit.

The api renders and seals each email inside the caller's transaction; the worker only connects and sends, reading the SMTP settings in effect at that moment.

**Implementation skills**: `email-best-practices` (`resend/email-best-practices`, `.claude/skills/email-best-practices/`) · `dotnet-webapi` (`dotnet/skills`, `.claude/skills/dotnet-webapi/`) · `ef-core` (`github/awesome-copilot`, `.claude/skills/ef-core/`) · `supabase-postgres-best-practices` (`supabase/agent-skills`, `.claude/skills/supabase-postgres-best-practices/`) · `dotnet-cryptography` (`envoydev/claude-stack`, `.claude/skills/dotnet-cryptography/`) · `security-and-hardening` (`addyosmani/agent-skills`, `.claude/skills/security-and-hardening/`) · `testcontainers-integration-tests` (`aaronontheweb/dotnet-skills`, `.claude/skills/testcontainers-integration-tests/`) · `configuring-opentelemetry-dotnet` (`dotnet/skills`, `.claude/skills/configuring-opentelemetry-dotnet/`) · `aspire` (`microsoft/aspire-skills`, `.claude/skills/aspire/`) · `tanstack-router` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-router/`) · `tanstack-query` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-query/`) · `tanstack-form` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-form/`) · `tanstack-table` (`tanstack-skills/tanstack-skills`, `.claude/skills/tanstack-table/`) · `zod` (`pproenca/dot-skills`, `.claude/skills/zod/`) · `shadcn` (`shadcn-ui/ui`, `.claude/skills/shadcn/`) · `accessibility` (`addyosmani/web-quality-skills`, `.claude/skills/accessibility/`) · `vitest` (`antfu/skills`, `.claude/skills/vitest/`) · `playwright-cli` (`microsoft/playwright-cli`, `.agents/skills/playwright-cli/`)

## Rationale

Reasoning, the options weighed, and the smaller calls made while writing: see [rationale.md](rationale.md).

## Feature design

### Stack additions

| Need | Choice | Notes |
|---|---|---|
| SMTP client | MailKit and MimeKit (MIT) | One `SmtpClient` per send, no pooling. Connected through a socket this module dials itself (AC-3), then `ConnectAsync(socket, host, port, options)` so TLS still checks the certificate against the host name. |
| Templates | Fluid (`Fluid.Core`, MIT), Liquid syntax | One shared `FluidParser`. `TemplateOptions`: no file provider, `MaxSteps = 100_000`, default recursion limit, a filter set of Liquid's standard filters minus `raw`, each string filter wrapped with a 1 MB result check, values passed as `FluidValue` objects built from dictionaries (no .NET object access), output written to a writer that throws past 1 MB. HTML part rendered with `HtmlEncoder.Default`, subject and text with no encoder. The validator walks the parsed statements for variable roots, property paths, filter names, and `include` or `render` tags. |
| HTML to text | Our own converter over MimeKit's `HtmlTokenizer` | Block elements and `<br>` become new lines, `<a href>` becomes `text (url)`, `<li>` becomes `- item`, `<h1>` to `<h6>` get a blank line after, `<script>`, `<style>`, and `<head>` are dropped, entities decoded, runs of blank lines collapsed to one. Unit tested. |
| Mail catcher (dev and tests) | Mailpit (MIT), image `axllent/mailpit` pinned by tag | AppHost resource, a `mailpit` service in the scenario compose, and a Testcontainers container in integration tests. |
| Console editor | CodeMirror 6 (`@codemirror/view`, `@codemirror/state`, `@codemirror/lang-html`, MIT) | Loaded only on the template editor route (`React.lazy`). |

### Module seams

`Orvano.Messaging` references `Orvano.Core`, `Orvano.Contract`, and `Orvano.Platform` (for `Contracts/` only). It never references Auth, so Auth (row 10) can reference Messaging without a cycle. Platform can't reference Messaging (that would be a cycle), so the invite email is inverted the same way spec 0008 inverted the console user directory.

| Contract | Owned by | Implemented by | Members | Used for |
|---|---|---|---|---|
| `IEmailQueue` | Messaging (`Contracts/`) | Messaging | `QueueAuthEmailAsync(NpgsqlTransaction tx, AuthEmail email, CancellationToken ct)` → `EmailQueueResult`; `CheckAvailabilityAsync(string projectId, CancellationToken ct)` → `EmailAvailability`, by the same SMTP resolution and AC-19 count queueing uses, on its own connection (spec 0010, AC-31) | row 10's flows; tested in this row. The availability check lets Auth refuse an open request before it reads an account, so the answer never depends on whether the account exists |
| `AuthEmail` | Messaging (`Contracts/`) | | `ProjectId`, `ProjectName` (the caller reads it with `IProjectDirectory.GetAsync` before opening its transaction, as `InvitationService` reads directory data first), `Kind` (`AuthEmailKind`: `Verification`, `Recovery`, `MagicLink`, `EmailCode`), `To`, `UserName` (nullable), `ActionUrl` (required for the three link kinds, null for `EmailCode`), `Code` (required for `EmailCode` only), `ExpiresInMinutes` (1 to 10,080) | |
| `EmailQueueResult` | Messaging (`Contracts/`) | | `Queued(Guid EmailId)` · `NotConfigured` · `RateLimited(TimeSpan RetryAfter)` | row 10 maps these to its own answers |
| `EmailAvailability` | Messaging (`Contracts/`) | | `Available` · `NotConfigured` · `RateLimited(TimeSpan RetryAfter)` (spec 0010) | row 10's open requests, mapped to 409 `email_not_configured` or 429 `email_rate_limited` |
| `IConsoleInvitationMailer` | Platform (`Contracts/`) | Messaging | `QueueAsync(NpgsqlTransaction tx, InvitationEmail email, CancellationToken ct)` → `bool` (queued or not) | `InvitationService.CreateAsync` |
| `InvitationEmail` | Platform (`Contracts/`) | | `To`, `OrgName`, `InviterName` (nullable), `InviterEmail`, `Role` (`OrgRole`), `Url`, `ExpiresAt` | |
| `ProjectInfo` (change) | Platform (`Contracts/`) | Platform | gains `Name` (its one constructor call is in `Application/PlatformDirectory.cs`) | the preview and test samples, the test email, and callers filling `AuthEmail.ProjectName` |

Messaging registers `IEmailQueue` and `IConsoleInvitationMailer` in `ConfigureApiServices` (they need `SecretBox` and `PublicUrl`); Platform resolves the mailer with `GetService` at request time, so an install without the module still works and simply never emails. Both write with raw Npgsql on the caller's `NpgsqlTransaction` (the effective SMTP read, the cap count, the insert) and write the job with `JobQueue.EnqueueAsync` on that same transaction, so no second EF context shares the caller's connection. Messaging's own endpoints and jobs use its internal `MessagingDbContext` through a `MessagingStore` shaped like `PlatformStore`. In code, the TypeSpec models `AuthEmailKind`, `EmailTemplateName`, and friends share names with Messaging's own types: alias the contract (`using Api = Orvano.Contract;`), as Platform does.

**Core change (jobs)**: new `JobRetryException(TimeSpan delay)` in `Orvano.Core.Jobs`. When a handler throws it, `JobStore.FailAsync` uses that delay in place of the exponential backoff (still going `dead` at `max_attempts`). The send handler uses it for AC-16's schedule, which the default backoff (2, 4, 8, 16 seconds) can't give. Row 31 (webhooks) will want the same.

### Data model sketch

One migration, `0005_messaging.sql`, and `MessagingDbContext` added to the drift check's context list. No foreign key reaches another module; the purge job cleans up by `project_id`.

**`orvano.messaging_smtp_settings`**: at most one row per project; the row for project `console` is the install SMTP.

| Column | Type | Rule |
|---|---|---|
| `project_id` | text PK | a project ID or `console` |
| `host` | text not null | 1 to 253 characters |
| `port` | integer not null | 1 to 65535 |
| `security` | text not null | `starttls`, `tls`, or `none` |
| `username` | text null | up to 256 |
| `password_ciphertext` | bytea null | SecretBox blob; `CHECK (password_ciphertext IS NULL OR username IS NOT NULL)` |
| `from_email` | text not null | up to 320 |
| `from_name` | text null | up to 128 |
| `reply_to` | text null | up to 320 |
| `updated_by_user_id` | uuid not null | the console user; no FK |
| `created_at`, `updated_at` | timestamptz not null | database clock |

`CHECK (security <> 'none' OR username IS NULL)` enforces AC-2's plaintext rule in the database too.

**`orvano.messaging_email_templates`**: a row only for a template you edited; defaults live in code (`Domain/DefaultTemplates`).

| Column | Type | Rule |
|---|---|---|
| `project_id` | text | part of PK |
| `kind` | text | part of PK; `verification`, `recovery`, `magic_link`, `email_code` |
| `locale` | text | part of PK; `CHECK (locale = 'en')` for now |
| `subject` | text not null | 1 to 255 characters |
| `html` | text not null | up to 102,400 bytes (`octet_length`) |
| `text` | text null | up to 102,400 bytes; null means derived from the HTML |
| `updated_by_user_id` | uuid not null | no FK |
| `created_at`, `updated_at` | timestamptz not null | |

**`orvano.messaging_emails`**: the queue and the 30 day log.

| Column | Type | Rule |
|---|---|---|
| `id` | uuid PK | set in code with `Guid.CreateVersion7()`; the only thing in the job payload |
| `project_id` | text not null | `console` for invites |
| `template` | text not null | `verification`, `recovery`, `magic_link`, `email_code`, `console_invitation` |
| `recipient_masked` | text not null | AC-22 |
| `content_ciphertext` | bytea null | SecretBox, associated data `messaging_emails:<id>:content_ciphertext`, sealing JSON `{to, subject, html, text, messageId}` |
| `status` | text not null default `queued` | `queued`, `sent`, `failed` |
| `smtp_source` | text null | `project` or `install`; set when sent |
| `attempts` | integer not null default 0 | set at the start of every attempt |
| `error_code` | text null | one of `EmailFailureCode` |
| `created_at` | timestamptz not null default now() | |
| `completed_at` | timestamptz null | |

Checks: `(status = 'queued') = (content_ciphertext IS NOT NULL)`; `(status = 'queued') = (completed_at IS NULL)`; `(status = 'failed') = (error_code IS NOT NULL)`; `smtp_source IS NULL OR status = 'sent'`. Indexes: `(project_id, created_at, id)` for the log and AC-19's count; `(created_at)` for retention, which deletes in batches of 1,000 rows so it never holds a long lock.

Relationships: each project has 0 or 1 SMTP row (else it uses the `console` row), 0 to 4 template rows per locale, and many email rows.

### State transitions

`messaging_emails.status`: `queued` → `sent` (the worker sent it) · `queued` → `failed` (a permanent error, the 6th failed attempt, `email_expired` from an attempt or from retention, `email_unreadable`, or `project_not_active`). Both are terminal, and each transition is one `UPDATE ... WHERE id = @id AND status = 'queued'` that checks the row count, so a duplicate job run (a lease that expired mid send) can never flip a final row.

### Template variables

The catalog lives in `Domain/EmailTemplateCatalog`. Variables are snake_case, as Liquid conventionally is. Row 10 supplies every value through `AuthEmail`.

| Variable | In | Description (shown in the editor) | Source at queue time | Sample (preview and test) |
|---|---|---|---|---|
| `project.name` | all four | Your project's name. | `AuthEmail.ProjectName` | the real project name (`ProjectInfo.Name`) |
| `user.email` | all four | The address this email is sent to. | `AuthEmail.To` | the caller's console email |
| `user.name` | all four | The user's name. Empty when they haven't set one. | `AuthEmail.UserName` (empty when null) | the caller's console name, else empty |
| `action_url` | `verification`, `recovery`, `magic_link` | The link the user opens to finish. | `AuthEmail.ActionUrl` | `https://example.com/auth/confirm?token=sample` |
| `code` | `email_code` | The code the user types in. | `AuthEmail.Code` | `428613` |
| `expires_in_minutes` | all four | How many minutes the link or code works. | `AuthEmail.ExpiresInMinutes` | 60 for links, 10 for `email_code` |

The templates themselves (names, descriptions, and default subjects; the default bodies are written at build to AC-13's rules):

| Kind | Name | Description | Default subject |
|---|---|---|---|
| `verification` | Email verification | Sent to confirm a user owns their email address. | `Verify your email for {{ project.name }}` |
| `recovery` | Password reset | Sent when a user asks to reset their password. | `Reset your password for {{ project.name }}` |
| `magic_link` | Magic link | Sent when a user signs in with a link instead of a password. | `Your sign in link for {{ project.name }}` |
| `email_code` | Email code | Sent when a user signs in with a one time code. | `Your sign in code for {{ project.name }}` |

The Log names `console_invitation` "Console invite". The invitation email is fixed in code (not editable, no Liquid), built from `InvitationEmail`.

### SMTP outcomes

One mapping in `Domain/SmtpOutcomes`, used by tests and by the worker. The endpoint never passes any other exception text through.

| What happened (MailKit) | Code | Worker | Test (HTTP) |
|---|---|---|---|
| DNS failure, `SocketException`, connection refused or reset | `smtp_unreachable` | retry | 502 |
| The attempt's budget ran out (`OperationCanceledException` from the linked timeout) | `smtp_timeout` | retry | 504 |
| `SslHandshakeException`, certificate invalid, or STARTTLS not offered | `smtp_tls_failed` | fail now | 502 |
| AUTH answered 5xx (`AuthenticationException`, for example 535) | `smtp_auth_failed` | fail now | 502 |
| AUTH answered 4xx (for example 454) | `smtp_auth_failed` | retry | 502 |
| MAIL, RCPT, or DATA answered 4xx (`SmtpCommandException`) | `smtp_rejected` | retry | 502 |
| MAIL, RCPT, or DATA answered 5xx | `smtp_rejected` | fail now | 502 |
| A resolved address is outside global unicast (AC-3) | `smtp_host_not_allowed` | fail now | 400 |

A row that fails after its 6th attempt stores the code of that last attempt.

### API surface

All operations are in the `console` audience (the private `@orvano/console-client`), so no public SDK changes. Project scoped ones read `X-Orvano-Project` and live under `/v1/console/project/`. New file `contract/messaging/email.tsp` (imported from `main.tsp`); the install operations go in `contract/platform/install.tsp` beside `getSetup`, served by Messaging's endpoints. SMTP and template writes are PUT (a full replace, safe to repeat), and the deletes answer 204 even when nothing is stored. The template `{kind}` path parameter is a string (contract parameters are primitive) that the endpoint checks against the four kinds, answering 404 `not_found` otherwise. The catalog operation is named `getCatalog`, not `list`, because SdkGen treats a `list` operation as a paged one.

| Operation | Method and path | Key inputs | Key outputs | Auth | Key errors |
|---|---|---|---|---|---|
| `consoleSmtp.get` | GET `/v1/console/project/email/smtp` | | `ProjectSmtp` | member | 404 `project_not_found` |
| `consoleSmtp.update` | PUT `/v1/console/project/email/smtp` | `SmtpSettingsInput` | `SmtpSettings` | owner, developer | 400 `invalid_request`, 400 `smtp_host_not_allowed`, 403, 409 `project_not_ready` |
| `consoleSmtp.delete` | DELETE `/v1/console/project/email/smtp` | | 204 | owner, developer | 403, 409 `project_not_ready` |
| `consoleSmtp.test` | POST `/v1/console/project/email/smtp/test` | `SmtpSettingsInput` | `EmailTestResult` | owner, developer | AC-6 codes, 400, 403, 409 `project_not_ready`, 429 |
| `consoleEmailTemplates.getCatalog` | GET `/v1/console/project/email/templates` | | `EmailTemplateCatalog` | member | 404 |
| `consoleEmailTemplates.get` | GET `/v1/console/project/email/templates/{kind}` | `kind: string` | `EmailTemplate` | member | 404 `project_not_found`, 404 `not_found` |
| `consoleEmailTemplates.update` | PUT `/v1/console/project/email/templates/{kind}` | `EmailTemplateInput` | `EmailTemplate` | owner, developer | 400, 422 `template_invalid`, 403, 409 |
| `consoleEmailTemplates.reset` | DELETE `/v1/console/project/email/templates/{kind}` | | 204 (also when already default) | owner, developer | 403, 409 |
| `consoleEmailTemplates.preview` | POST `/v1/console/project/email/templates/{kind}/preview` | `EmailTemplateInput` | `RenderedEmail` | owner, developer | 400, 422, 403, 429 |
| `consoleEmailTemplates.test` | POST `/v1/console/project/email/templates/{kind}/test` | `EmailTemplateInput` | `EmailTestResult` | owner, developer | 400, 422, 409 `project_not_ready`, 409 `email_not_configured`, AC-6 codes, 403, 429 |
| `consoleEmails.list` | GET `/v1/console/project/emails` | `cursor?`, `limit?` | `EmailPage` | member | 404, 400 `invalid_cursor` |
| `consoleInstall.getSmtp` | GET `/v1/console/install/smtp` | | `InstallSmtp` | install admin | as `updateSettings` for others |
| `consoleInstall.updateSmtp` | PUT `/v1/console/install/smtp` | `SmtpSettingsInput` | `SmtpSettings` | install admin | 400 |
| `consoleInstall.deleteSmtp` | DELETE `/v1/console/install/smtp` | | 204 | install admin | 403 |
| `consoleInstall.testSmtp` | POST `/v1/console/install/smtp/test` | `SmtpSettingsInput` | `EmailTestResult` | install admin | AC-6 codes, 429 |
| `consoleInstall.listEmails` | GET `/v1/console/install/emails` | `cursor?`, `limit?` | `EmailPage` | install admin | 400 `invalid_cursor` |
| `consoleInvitations.create` (change) | as today | as today | `CreatedInvitation` + `emailed: boolean` | as today | as today |

Models:
- `SmtpSecurity` = `starttls` | `tls` | `none`; `SmtpSource` = `project` | `install` | `none`; `AuthEmailKind` = `verification` | `recovery` | `magic_link` | `email_code`; `EmailTemplateName` = the four kinds plus `console_invitation`; `EmailStatus` = `queued` | `sent` | `failed`; `EmailFailureCode` = `smtp_unreachable` | `smtp_tls_failed` | `smtp_auth_failed` | `smtp_rejected` | `smtp_timeout` | `smtp_host_not_allowed` | `email_not_configured` | `project_not_active` | `email_expired` | `email_unreadable`.
- `SmtpSettingsInput` (`host`, `port: int32`, `security`, `username: string | null`, `password: string | null`, `fromEmail`, `fromName: string | null`, `replyTo: string | null`).
- `SmtpSettings` (the same without `password`, plus `hasPassword: boolean`, `updatedAt`).
- `ProjectSmtp` (`source`, `settings: SmtpSettings | null`, `installSender: EmailSender | null`); `EmailSender` (`email`, `name: string | null`); `InstallSmtp` (`settings: SmtpSettings | null`).
- `EmailTestResult` (`sentTo`).
- `EmailTemplateInput` (`subject`, `html`, `text: string | null`); `EmailTemplate` (`kind`, `locale`, `subject`, `html`, `text: string | null`, `isCustom: boolean`, `updatedAt: utcDateTime | null`, `variables: TemplateVariable[]`); `TemplateVariable` (`name`, `description`, `sample`); `EmailTemplateCatalog` (`templates: EmailTemplateSummary[]`, not paged: always four); `EmailTemplateSummary` (`kind`, `name`, `description`, `isCustom`, `updatedAt: utcDateTime | null`); `RenderedEmail` (`subject`, `html`, `text`).
- `EmailLogEntry` (`id`, `template: EmailTemplateName`, `recipient` (masked), `status`, `smtpSource: SmtpSource | null`, `attempts`, `errorCode: EmailFailureCode | null`, `createdAt`, `completedAt: utcDateTime | null`); `EmailPage` (`items`, `nextCursor`).

New error codes in `contract/errors.tsp`: `smtp_host_not_allowed` (400), `smtp_unreachable` (502), `smtp_tls_failed` (502), `smtp_auth_failed` (502), `smtp_rejected` (502), `smtp_timeout` (504), `template_invalid` (422), `email_not_configured` (409), `email_rate_limited` (429; unused by this row's operations, reserved for row 10 to answer `RateLimited` with). Problem `detail` for the SMTP codes is safe to show: for `smtp_rejected` it holds the server's reply code and text (at most 200 characters); the others hold a fixed sentence. The endpoint maps MailKit's exceptions through the *SMTP outcomes* table and never passes any other exception text through.

New rate limit policies in `RateLimitPolicies`: `messaging.test.user`, 30 per 15 minutes, and `messaging.preview.user`, 300 per 5 minutes, both keyed by console user ID and counting every attempt that passed the role check (AC-25).

### Screens

- **Project sidebar**: a new "Email" item (lucide `Mail`) below Users. Route `/projects/$projectId/email` redirects to `email/settings`; tabs Settings, Templates, and Log are nested routes (`email/settings`, `email/templates`, `email/templates/$kind`, `email/log`) so each deep links. Document titles follow the existing pattern (`Email settings · <project> · Orvano`).
- **Settings tab**: AC-4's source line, then the form: Host, Port, Security (radio group: "STARTTLS (usually port 587)", "TLS (usually port 465)", "None"), Username, Password (placeholder "Saved. Leave empty to keep it." when `hasPassword`), From email, From name, Reply to. An empty form starts on STARTTLS with Port empty; choosing Security fills Port with 587, 465, or 25 only while Port is still empty. Buttons: Send test email, Save (disabled until something changed), and "Stop using these settings". Viewers see the form read only with no password field.
- **Templates tab and editor**: AC-8 and AC-9. The editor is a two column layout on wide screens (editors left, preview right) and stacked on narrow ones. The preview has Subject, HTML, and Text views. Variables are listed with a copy button each.
- **Log tab**: AC-20's `DataTable`. Reason messages by `errorCode`:

| Code | Message |
|---|---|
| `smtp_unreachable` | Couldn't connect to the SMTP server. |
| `smtp_tls_failed` | The secure connection failed. Check the Security setting and the server's certificate. |
| `smtp_auth_failed` | The SMTP server refused the username or password. |
| `smtp_rejected` | The SMTP server refused this email. |
| `smtp_timeout` | The SMTP server didn't answer in time. |
| `smtp_host_not_allowed` | The SMTP host points to a private network address, which projects can't use. |
| `email_not_configured` | No email server was set up when it was sent. |
| `project_not_active` | Not sent: the project was deleted. |
| `email_expired` | Not sent: it waited more than 30 minutes in the queue. |
| `email_unreadable` | Not sent: the server couldn't decrypt it. The master key may have changed. |

- **`/install`**: an "Email server" card (the same form component, no source line, starting on STARTTLS) and a "Console emails" card with the same log table (AC-7, AC-21).
- **Invite dialog link step**: AC-23's extra line when `emailed` is true.

### Preview frame

A `srcdoc` frame inherits its parent's Content Security Policy whatever its `sandbox` says, so the old frame ran under the console's `style-src 'self'` and `img-src 'self' data:` and lost every inline style and remote image behind the gateway. The preview now gets a page of its own, served with a policy written for email (AC-31, AC-32).

| Piece | Where | What it does |
|---|---|---|
| Frame page | `console/public/frames/email-preview.html` | Plain HTML, not processed by Vite, so its bytes (and the script's hash) are what Caddy serves. One inline `<style>` (no margin, white, `color-scheme: light`, the inner frame at full width and height), the static inner frame, and one inline `<script>`: post ready, check each message, clean a parsed copy, set the `srcdoc` (AC-31). No imports, no other files. Listed in `.prettierignore`, and `.gitattributes` gives it and the `.caddy` file `eol=lf`, so formatting or a Windows checkout never changes the hashed bytes. |
| Editor side | `-email/template-editor.tsx` (a small `EmailPreviewFrame` component beside it) | AC-33: keeps the last successful HTML in a ref (TanStack Query's `keepPreviousData` drops it on an error), the handshake, the change check, the 5 second timeout; the HTML `Tabs.Panel` gets `keepMounted`. Target origin `"*"` is required (an opaque origin can't be named) and safe: the message goes to the outer frame's own `contentWindow`, its `src` is fixed, and its sandbox stops anything inside from navigating it elsewhere. |
| Headers | `deploy/gateway/email-preview-headers.caddy` | Every AC-32 header, one per line, as `Name "value"` (double quotes, no escapes inside; blank lines and `#` comments allowed). Inside the console `handle`, a nested `handle /frames/email-preview.html { header { import email-preview-headers.caddy } … file_server }` runs after the outer `header` block, so its fields replace the console's for that path whatever the directive order. The import is relative to the Caddyfile; the gateway Dockerfile copies the file to `/etc/caddy/`. The gateway image build runs `caddy validate` on the result. |
| Dev headers | a small plugin in `console/vite.config.ts` | `configureServer` and `configurePreviewServer` (never the build): reads the `.caddy` file with the grammar above and sets every header on `/frames/email-preview.html`. A parse that finds no `Content-Security-Policy` fails startup with the file's path. The Vitest browser project (`vitest.config.ts`) does not load it, which is fine: it serves the page from `public/`, still opaque by the iframe's sandbox. |
| Hash check | a Vitest node test in `console/` | Asserts the frame page has exactly one inline `<script>` and no `src` script, hashes the exact bytes between its tags (no trim; SHA-256, base64), and asserts the `.caddy` file's `script-src` carries exactly that hash, so editing the script without updating the policy fails the unit run before the end to end test does. |
| Tests | `email-templates.browser.test.tsx`, `e2e/` | The component test can't see inside an opaque frame: it stubs the outer frame's `contentWindow` with a `postMessage` spy, dispatches `MessageEvent`s with that stub as `source`, and asserts what is posted (first `ready`, a change, an unchanged HTML, a foreign `source`, the timeout). Policy, styles, link handling, the "Email content" title, and violations are checked in Playwright through nested `frameLocator`s, collecting `page.on('console')` errors that mention Content Security Policy, attached before `goto`. AC-30's axe scope is the console page and the outer frame; axe can't enter an opaque frame and reports it as incomplete, not a violation. |

### Value sourcing

| Action | Value produced or displayed | Source |
|---|---|---|
| `consoleSmtp.get` | `source` | derived: project's row exists → `project`; else `console` row exists → `install`; else `none` |
| `consoleSmtp.get` | `installSender` | `console` row's `from_email`, `from_name`, whenever that row exists (null otherwise) |
| Stop using confirm | which sentence to show | `installSender` is set or null |
| `consoleSmtp.get` / `update` | `hasPassword` | `password_ciphertext IS NOT NULL` |
| `consoleSmtp.update` | the stored password when `password` is null | the existing `password_ciphertext`, kept only when `host`, `port`, and `username` equal the stored row's (AC-1) |
| `consoleSmtp.update` | `updated_by_user_id`, the event's actor | `ConsoleUser` from the session |
| Any test send | `sentTo` | the caller's email: `IConsoleUserDirectory.GetManyAsync([ConsoleUser.Id])` (spec 0008) |
| `consoleSmtp.test` | the SMTP settings used | the request body; `password: null` → the stored password of that same row (project, or `console` for the install), under AC-1's same host, port, and username rule |
| Test email body | project name | `ProjectInfo.Name` (new); "this Orvano server" for the install test |
| `consoleEmailTemplates.get` | `isCustom`, `updatedAt` | a template row exists; its `updated_at` |
| `consoleEmailTemplates.get` | `subject`, `html`, `text` when not custom | `Domain/DefaultTemplates` |
| `consoleEmailTemplates.get` | `variables` | `Domain/EmailTemplateCatalog` for that kind |
| `preview` / template `test` | sample values | the catalog's samples, with `project.name`, `user.email`, and `user.name` from the real project and caller |
| `preview` | `text` when the input's is null | `HtmlToText(rendered html)` |
| Preview frame | the HTML shown | `RenderedEmail.html` from the latest successful `.preview` answer, held in `EmailPreviewFrame`'s ref and posted to the frame page (AC-31, AC-33) |
| Preview frame | where links open, which links work | the frame page's cleaned copy: `<base target="_blank">`, `href` kept only for `http`, `https`, `mailto`, `rel="noopener noreferrer"` |
| Preview frame | the referrer images send | none: `Referrer-Policy: no-referrer`, the template's `<meta name="referrer">` removed, `referrerpolicy="no-referrer"` on each `img` |
| Frame page policy | the `script-src` hash | SHA-256 of the page's one inline script, written into `email-preview-headers.caddy` and checked by the hash test |
| `QueueAuthEmailAsync` | effective SMTP (to decide `NotConfigured` and the cap) | the project's row, else the `console` row, read in the caller's transaction (`CheckAvailabilityAsync` resolves it the same way, on its own connection, spec 0010) |
| `QueueAuthEmailAsync` | the cap's count and `retryAfter` (`CheckAvailabilityAsync` uses the same count, spec 0010) | `count(*)` of the project's rows with `created_at > now() - interval '1 hour'`; `retryAfter` from the `created_at` at offset `count − limit` in that set, ordered ascending, plus one hour, minus `now()` |
| `QueueAuthEmailAsync` | the limit | `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` |
| `QueueAuthEmailAsync` | the rendered email | the custom row (else the default) rendered with `AuthEmail`, `project.name` from `AuthEmail.ProjectName` |
| `QueueAuthEmailAsync` | the email ID | `Guid.CreateVersion7()` |
| `QueueAuthEmailAsync` | `Message-ID` host | `PublicUrl` host (api only, which is why rendering happens at queue time) |
| `QueueAuthEmailAsync` | `recipient_masked` | AC-22 applied to `AuthEmail.To` |
| Send attempt | SMTP settings, From, Reply to | the effective SMTP at attempt time |
| Send attempt | project still active | `IProjectDirectory.GetAsync(projectId).Status == Active` |
| Send attempt | retry delay | `ClaimedJob.Attempts`: 1 → 30 s, 2 → 1 min, 3 → 2 min, 4 → 4 min, 5 → 8 min |
| Send attempt | stale check | `now()` minus `created_at` from the row |
| Send attempt | `attempts` | `ClaimedJob.Attempts` |
| Send attempt | the failure code | the *SMTP outcomes* table; after the 6th attempt, that attempt's code |
| Invite email | org name, inviter name and email, role, url, expiry | what `InvitationService.CreateAsync` already has: the org row, `IConsoleUserDirectory`, the request's role, the url it returns, the invitation's `expires_at` |
| `consoleInvitations.create` | `emailed` | `IConsoleInvitationMailer.QueueAsync`'s result (`false` for no install SMTP or the cap), `false` when no mailer is registered |
| Invite email | expiry text | `expires_at` formatted `MMM d, yyyy, HH:mm 'UTC'`, invariant culture |
| Log table | Created, Completed in local time | `createdAt`, `completedAt` (UTC) formatted with the browser's time zone (`Intl.DateTimeFormat`) |
| Log table | Reason | the console's message table above, keyed by `errorCode` |

### Key invariants

- An email row and its job exist only if the change that asked for them commits (same transaction).
- `content_ciphertext` is set exactly while `status` is `queued` (database check); final rows keep no recipient, link, or code.
- A final status never changes (conditional update on `status = 'queued'`).
- The password is only ever stored sealed and is never returned; `security = 'none'` never has a username (database check).
- Project SMTP connections dial only addresses that passed AC-3's check, checked at every connection.
- Every Liquid render uses the same `TemplateOptions` (AC-11), and every `{{ }}` output in HTML is encoded: the `raw` filter does not exist.
- The worker never renders; it only unseals and sends. The api never sends a queued email; it only sends tests.
- Retention and purge are the only deletes of `messaging_emails`.
- Template HTML never runs script and never shares the console's origin: it renders only in a frame without `allow-scripts`, inside a page with an opaque origin. The frame page is the one console file with its own policy.

### Security model

- **Roles** (spec 0008's console roles): AC-24's matrix for project operations; install operations for the install admin only (`IInstallAdmins`). The Log shows masked recipients to every member, since viewers already see app user emails on the Users page.
- **Secrets**: SMTP passwords and queued content are sealed with `SecretBox` (spec 0002), so a database dump or backup exposes neither a credential nor a working link or code (spec 0003's rule). `realtime` never needs them.
- **SSRF** (server side request forgery, making the server connect somewhere it shouldn't): AC-3, applied at connection time; install SMTP exempt because the install admin already controls the server.
- **Spam relay**: test sends go only to the caller's own console email, and are rate limited (AC-25). Real sends are only triggered by Orvano's own flows, and sends through the shared install SMTP are capped per project, console invites included (AC-19), so open console sign up can't turn invites into a mass mailer.
- **Credential capture**: a stored SMTP password is reused only for the same host, port, and username (AC-1), so someone who can edit settings but never saw the password can't redirect it to a server they run.
- **Template injection**: Liquid has no code or file access; values are encoded in HTML (so an app user's name can't inject markup into another project's email), subjects are cleaned of control characters (no header injection), and the console preview renders the HTML in a frame with no scripts, inside a page with an opaque origin (AC-31).
- **Preview frame**: a developer's template can't attack an owner who previews it. Its scripts never run (no `allow-scripts`, and the inherited policy allows only the frame page's own hash), it can't reach the console's cookies, storage, or page (opaque origin), it can't navigate the console (no top navigation), and frames and forms inside it are blocked. Only `http`, `https`, and `mailto` links survive, and they open with no opener, so `allow-popups-to-escape-sandbox` (the one flag that lifts the sandbox) can't carry a `javascript:` link out. Remote images do load: the image host sees the previewing member's IP address, but no `Referer` (AC-31, AC-32). A relative URL in a template resolves against the console's origin, so console GET routes must stay free of side effects (they are today; this frame relies on it). Remote stylesheets and fonts are blocked, as Gmail and Outlook drop them.
- **Personal data**: recipient addresses are personal data. Only the masked form survives past delivery, for 30 days; events and logs carry none (AC-26). The project purge removes everything (AC-27).

### Configuration required

- `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS` (`api`, `worker`; read in `MessagingModule.ConfigureServices`, so `api`, `worker`, and `realtime` validate it): `true` lets project SMTP use private addresses. Default `false`. The AppHost and the scenario compose set `true` for Mailpit.
- `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` (used by `api`, validated by `api`, `worker`, and `realtime`): emails per project per hour through the install SMTP, 1 to 1,000,000, default 200.
- Unset or empty means the default for both. No new setting is required for production. The embedded `deploy/compose/docker-compose.yml` (which `orvano install` writes to every install) passes both to `api` and `worker` as `${VAR:-}`, and the installer does not prompt for them.

### Critical test scenarios

- Happy path, SMTP: a developer saves project SMTP pointing at Mailpit, sends a test, and the test email arrives in Mailpit addressed to their console email, multipart, from the configured sender, verifies **AC-1**, **AC-6**.
- Happy path, templates: edit the recovery template, preview shows the sample project name HTML encoded, save, send test, and it arrives with the edited subject, verifies **AC-9**, **AC-11**, **AC-12**.
- Happy path, queue: `QueueAuthEmailAsync` in a committed transaction queues one row and one job; the worker sends it to Mailpit; the row ends `sent` with null content; in a rolled back transaction nothing exists, verifies **AC-14**, **AC-15**.
- Invite: with install SMTP set, create an invitation: the response has `emailed: true`, the email arrives with the same url the dialog shows; with none, `emailed: false` and nothing is queued, verifies **AC-23**.
- Failure, retries: Mailpit answering 451 then accepting retries on schedule and ends `sent`; a wrong password fails at once with `smtp_auth_failed`; changing the password while an email waits sends it on the next attempt; an attempt 31 minutes after queueing fails with `email_expired`, verifies **AC-15**, **AC-16**, **AC-17**.
- Failure, SSRF: project SMTP host `127.0.0.1`, `10.0.0.5`, and a name resolving to `169.254.169.254` are refused on update, test, and send, and allowed with `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=true` and for the install SMTP, verifies **AC-3**.
- Failure, templates: an unknown variable, a syntax error, `{% include %}`, and a loop over 100,000 steps each answer 422 with the line, verifies **AC-10**.
- Failure, cap: with a limit of 2, the third queue in an hour for a project using the install SMTP returns `RateLimited` with the right `retryAfter`; a project with its own SMTP is not capped; an invite over the cap is created with `emailed: false`, verifies **AC-19**, **AC-23**.
- Failure, credentials: changing only the host with `password: null` answers 400 on update and test, and the stored password never leaves for the new host, verifies **AC-1**.
- Failure, stuck and unreadable: a queued row whose job the reaper marked `dead` is failed with `email_expired` by retention; content sealed under a removed master key fails with `email_unreadable`; a job whose row was purged completes as a no op, verifies **AC-15**, **AC-17**.
- Failure, template limits: `| raw`, an unknown filter, `user.emial`, and a string doubled in a loop past 1 MB each answer 422, verifies **AC-10**, **AC-11**.
- Preview frame, production shape: behind the gateway, the default password reset preview shows the button styled and at least 44 px tall and the preheader hidden; a `<style>` block, an `https:` image, and a `data:` image apply, while a remote stylesheet, a `<script>`, and an `onclick` do not; clicking an `https` link opens a new page with no opener, and clicking a `javascript:` link opens nothing and runs nothing; a subject only edit doesn't reload the frame, and a 422 keeps the last preview dimmed; the "Email content" title is present; no frame logs a policy violation for the default templates; `/frames/email-preview.html` carries AC-32's headers while `/` keeps the console's, verifies **AC-31**, **AC-32**, **AC-33**.
- Auth and permission: a viewer reads settings, templates, and the log but gets 403 on update, preview, and test; a non member gets 404; a non admin gets the non admin answer on install operations; the 31st test in 15 minutes gets 429, verifies **AC-7**, **AC-24**, **AC-25**.
- Leaks: after a send, `orvano.jobs`, `orvano.events`, and captured logs contain no address, host, username, password, subject, or link, verifies **AC-26**.

## Build plan

Tracer Bullet: the first slice pushes one test email from the project's Settings tab through the contract, the server, and MailKit into Mailpit, end to end; the second sends the first real queued email (a console invite) through the worker; later slices thicken it.

1. **Thin thread: project SMTP and a test email that arrives.** `Orvano.Messaging` csproj (MailKit, MimeKit), `MessagingModule` in `OrvanoModules.Product`, the server Dockerfile's csproj copy; migration `0005_messaging.sql` with all three tables and `MessagingDbContext` in the drift check; `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS` and `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` validation, and both in the embedded compose file; `Domain/` rules with unit tests (settings validation with Messaging's copy of the email rule and the host syntax, the password reuse rule, the global unicast rule, `SmtpOutcomes`); `SmtpConnector` (resolve, check, dial in order with the linked budget, `ConnectAsync` with the socket) and `SmtpSender` over MailKit; `ProjectInfo.Name` and its `PlatformDirectory` constructor call; `contract/messaging/email.tsp` with the `consoleSmtp.*` operations, the new error codes, and SdkGen; the endpoints with `IConsoleAccess`, AC-24's check order, and the `messaging.test.user` limit; `messaging.smtp.*` events; Mailpit in the AppHost; the console Email route, sidebar item, and Settings tab. Satisfies **AC-1**, **AC-2**, **AC-3**, **AC-4**, **AC-5**, **AC-6**, **AC-24**, **AC-25**, **AC-28**, **AC-29** (AppHost part).
2. **The queue, through console invites.** `JobRetryException` in Core with its `JobStore` test; `EmailSealer` (seal and unseal content), recipient masking, `HtmlToText` with unit tests; the shared queue core (effective SMTP read, AC-19's soft cap and `retryAfter`, ID, Message-ID, insert, and job, in raw Npgsql on the caller's transaction); the invitation email built in code (HTML and text); `IConsoleInvitationMailer` and `InvitationEmail` in Platform `Contracts/`, Messaging's implementation, `InvitationService.CreateAsync` calling it, and `CreatedInvitation.emailed`; the `messaging.email.send` handler with AC-15 to 17's rules (no op on a missing or final row, `attempts` per attempt, `email_unreadable`), `messaging.email.*` events, and OpenTelemetry counters (`orvano.messaging.emails.sent`, `.failed`, tagged by template, SMTP source, and error code) plus a send duration histogram; `consoleInstall.getSmtp`, `.updateSmtp`, `.deleteSmtp`, `.testSmtp`, `.listEmails`; `consoleEmails.list`; the hourly retention schedule (batched deletes, and failing stuck `queued` rows); the `messaging.purge_project` consumer and `messaging.project.purge`; the scenario compose `mailpit` service and install SMTP fixture; the console Log tab, the `/install` Email server and Console emails cards, and the invite dialog line. Satisfies **AC-7**, **AC-15**, **AC-16**, **AC-17**, **AC-19**, **AC-20**, **AC-21**, **AC-22**, **AC-23**, **AC-26**, **AC-27**, **AC-29** (compose part).
3. **Templates and the auth queue.** Fluid with AC-11's options (the filter set without `raw`, size checked string filters, the bounded writer); `EmailTemplateCatalog` with the names, descriptions, and default subjects above, the four `DefaultTemplates` (HTML and text, AC-13's rules), and the validator (parse, walk the statements for variable roots, property paths, filters, and tags against the catalog, sample render within limits) with unit tests; `IEmailQueue.QueueAuthEmailAsync` with its input checks and the render fallback on top of slice 2's queue core, and its integration tests on real Postgres (commit, rollback, `NotConfigured`, `RateLimited`); the `consoleEmailTemplates.*` operations and the `messaging.preview.user` limit; `messaging.template.*` events; the Templates tab and the lazy loaded CodeMirror editor with preview, save, reset, send test, the viewer's read only view, and the unsaved changes prompt. Satisfies **AC-8**, **AC-9**, **AC-10**, **AC-11**, **AC-12**, **AC-13**, **AC-14**, **AC-18**.
4. **Preview frame (found by slice 3's verify).** `console/public/frames/email-preview.html` with its inline style, static inner frame, and script, plus its `.prettierignore` and `.gitattributes` lines; `EmailPreviewFrame` beside the editor, replacing the `srcdoc` iframe, with AC-33's lifecycle and `keepMounted` on the HTML tab; `deploy/gateway/email-preview-headers.caddy`, the nested Caddyfile `handle` that imports it, the gateway Dockerfile copy, and `caddy validate` in the image build; the Vite dev and preview plugin; the hash test; a row for the frame page's policy in `console/design.md`'s Security exception log; `email-templates.browser.test.tsx` updated for the two frames with a stubbed `contentWindow`; `e2e/headers.spec.ts` checking the frame page's headers and that the console's are unchanged; `e2e/email-templates.spec.ts` asserting styles inside the frame and collecting policy violations from every frame (`page.on('console')`, since a page listener never sees a frame's violations). Satisfies **AC-9**, **AC-30** (the frame titles), **AC-31**, **AC-32**, **AC-33**.
5. **Quality and end to end.** Integration tests with Mailpit in Testcontainers for every scenario above, including the leak check; Playwright flows in the `console` compose profile (set SMTP, test, edit a template, invite by email, read each from Mailpit's API); browser tests with axe in both themes and densities, keyboard runs through the editors; document titles. Satisfies **AC-30**, and re checks **AC-1** to **AC-33** end to end.

## Consequences

**Positive**:
- Every project can send email the moment the install admin sets one SMTP server, and any project can switch to its own without touching the others.
- Row 10 only triggers emails: templates, the editor, delivery, retries, and the log already exist, and it gets one call that is safe inside its own transaction.
- Console invites finally arrive by email, closing spec 0008's follow up without weakening its token rules.
- A database dump holds no SMTP password and no working link or code, and the log keeps only masked addresses.
- Row 24 and row 26 grow the same module, templates engine, and queue instead of building a second email path.

**Negative / tradeoffs**:
- Delivery is at least once: if the worker dies after the SMTP server accepted an email but before the row is marked `sent`, the retry sends it again. The stable `Message-ID` lets some mail systems drop the copy; most will show two.
- A retry window of about 15 minutes, and the 30 minute stale rule, mean a long SMTP outage loses emails instead of delivering them late. Users must ask again (the right trade for codes and links; a small cost for invites, which have Resend).
- An email queued for an invitation that is replaced or revoked a moment later still goes out, with a link that no longer works.
- Templates are last write wins between two editors; there is no conflict check.
- The install cap is soft: requests at the same moment can pass it by a few emails.
- "Delivered" here means the SMTP server accepted it. Bounces, spam folder placement, and complaints are invisible until row 24 adds provider webhooks.
- SMTP connections are opened per email with no pooling, which is fine for auth volume but not for bulk sending.
- The api holds outbound SMTP connections during tests for up to 20 seconds each, limited by the per user rate limit.
- The install SMTP is shared, so one project's abuse still spends the install's sender reputation up to the hourly cap.
- Templates are English only until a user locale exists; the `locale` column is ready, the UI is not.
- The rate limit, like every other, is in memory per `api` process (spec 0002).
- The preview frame page is a second Content Security Policy to keep correct, with a script hash that changes whenever its script does (the hash test catches it; the fix is the new hash in one file).
- Previewing a template with remote images reveals the previewing member's IP address to the image host, on every render that changes the image.
- The preview shows inline and `<style>` CSS only: a template that relies on a remote stylesheet or web font looks plainer in the preview than in Apple Mail.
- A link the email sends to `target="_self"` or another named frame does nothing in the preview (frames are blocked).

**Neutral**:
- A new module, `Orvano.Messaging`, and a new contract folder, `contract/messaging/`.
- A Core addition (`JobRetryException`) that later rows can use.
- One console file, `/frames/email-preview.html`, served with its own policy; row 26's workflow email previews can reuse it.
- `ProjectInfo` gains `Name`, a small change to spec 0003's module contract.
- New dependencies: MailKit, MimeKit, and Fluid on the server; CodeMirror 6 in the console; Mailpit in dev and tests only. All MIT, so Apache 2.0 compatible.

## Follow-up

- [x] Row 10 (email verification, recovery, and passwordless): done in [spec 0010](../0010-email-verification-recovery-passwordless/index.md), which also amended AC-14 and *Module seams* here. Read the project name before its transaction and call `IEmailQueue.QueueAuthEmailAsync`, choose each flow's expiry, map `NotConfigured` and `RateLimited` to its answers (`email_not_configured`, `email_rate_limited`), and add per recipient limits for resend.
- [ ] Row 14 (auth policies and abuse protection): consider a per recipient limit on console invite emails, which needs a hash of the recipient that this row doesn't store.
- [ ] Spec 0008: when this row is built, mark its row 9 follow up done.
- [ ] Row 24 (messaging providers): generalize the SMTP row into providers (HTTP APIs such as SES or Postmark), add bounce and complaint webhooks and a suppression list, and a server SDK send operation.
- [ ] Per locale templates once app users carry a locale (the `locale` column is already in the key).
- [ ] Row 11 (docs site): a page on setting up SMTP, including SPF, DKIM, and DMARC (the DNS records that prove your domain may send), and not using a `noreply@` address.
- [ ] `/sync`: add `server/src/Orvano.Messaging/AGENTS.md`, a line for it in `server/AGENTS.md`, and the `email-best-practices` skill (`resend/email-best-practices`, `.claude/skills/email-best-practices/`) in the right `## Agent skills` section (the Messaging area, since it only governs email). It was installed during this design and is not in any `AGENTS.md` yet.
- [ ] Spec 0005's security headers section: note that `/frames/email-preview.html` is the one console file with its own policy (spec 0009, AC-32) when slice 4 ships.
- [ ] Update spec 0003's module contract note for `ProjectInfo.Name`, and spec 0002's jobs section for `JobRetryException`, when they ship.
