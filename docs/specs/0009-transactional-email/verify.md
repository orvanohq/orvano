# Verify: Transactional email · spec 0009 · updated 2026-09-30

_Steps derived from spec 0009 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

This file covers slices 1 to 3 of the build plan so far. Slice 1 (project SMTP and a test email that arrives): AC-1 to AC-6, AC-24, AC-25, AC-28, and the AppHost part of AC-29. Slices 2 and 3 have their own sections below. The last slice adds its own steps.

Setup: `dotnet run --project dev/Orvano.AppHost`, open the console, create the first admin on `/setup`, make an org and a project, and wait until the project is active. In the Aspire dashboard, note Mailpit's `smtp` port and open its `http` endpoint to read mail. Add a second console account as a viewer of the org for the role steps.

## UI / manual

- [ ] Open the project, choose Email in the sidebar (below Users) → the address becomes `/projects/<id>/email/settings`, the Settings tab is current, and the document title is `Email settings · <project> · Orvano` → Screens
- [ ] With no SMTP anywhere → the warning "No email server is set up" shows above an empty form that starts on STARTTLS with Port empty → AC-4 (`source: none`)
- [ ] Choose TLS while Port is empty → Port becomes 465; type 2525, then choose None → Port stays 2525 → Screens
- [ ] Fill Host `localhost`, Port (Mailpit's smtp port), Security None, From email `hello@shop.test`, From name `Shop`, then Send test email before saving → "Sent to <your console email>. Check your inbox." and Mailpit shows "Test email from Orvano" from `Shop <hello@shop.test>`, with a text part and an HTML part that both name the project → AC-6, value sourcing (`sentTo`, test email body, settings used)
- [ ] Rename the project, send the test again → the new email names the new project name → value sourcing (`ProjectInfo.Name`)
- [ ] Save → a success toast, Save turns off ("Nothing to save yet"), "Stop using these settings" appears; reload → the form is filled from the saved settings → AC-1, AC-4 (`source: project`)
- [ ] Change Host to `bad host!` and Save → "Enter a host name or an IP address of at most 253 characters." shows under Host and is announced → AC-2
- [ ] Set Security None, type a Username, Save → "A username needs STARTTLS or TLS." shows under Username → AC-2
- [ ] Press Send test email with a Port nothing listens on → the alert says "Couldn't connect to the SMTP server." and no other server text → AC-6 (`smtp_unreachable`)
- [ ] Press "Stop using these settings" → the confirm says "This project won't be able to send email." (no install SMTP yet), focus starts on Cancel; confirm → the tab shows the "No email server is set up" warning and an empty form → AC-5
- [ ] Sign in as the viewer → every field is read only, there is no Password field, and Save, Send test email, and Stop using these settings are off with "Developers and owners only" → AC-24
- [ ] Check the page at 375 px wide, in light and dark, and with keyboard only (Tab through every field, arrows in the Security group) → nothing overflows, focus is always visible → Screens

## Commands

- [x] `dotnet test --solution Orvano.slnx` → all pass; `Orvano.Server.Tests.Messaging` covers the rules and the HTTP path against Mailpit → AC-1 to AC-6, AC-24, AC-25, AC-28 · checked 2026-09-30: 1,100 of 1,100 passed
- [ ] `PUT /v1/console/project/email/smtp` with a username and password, then `GET` → `hasPassword: true` and no password in either body; `SELECT password_ciphertext FROM orvano.messaging_smtp_settings` is a blob that does not contain the password; `SELECT payload FROM orvano.events WHERE type = 'messaging.smtp.updated'` holds `projectId`, `actor`, and `changed` only → AC-1, value sourcing (`hasPassword`, `updated_by_user_id`)
- [ ] `PUT` again with `password: null` and the same host, port, and username → 200 and the same ciphertext; change only `host` with `password: null` → 400 `invalid_request`, `password: Enter the password again when you change the host, port, or username`, on update and on test → AC-1, value sourcing (stored password)
- [ ] Start `api` without `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS`; `PUT` and `POST .../test` with host `127.0.0.1`, `10.0.0.5`, `169.254.169.254`, `::ffff:10.0.0.5`, and `localhost` → 400 `smtp_host_not_allowed` each; a host that does not resolve saves with 200 and its test answers 502 `smtp_unreachable` → AC-3
- [ ] With the install row present (`INSERT INTO orvano.messaging_smtp_settings` for project `console`) and no project row → `GET` returns `source: install`, `settings: null`, and `installSender` with only the From email and name; the tab shows "Using this server's email settings"; with a project row too, `source: project` and `installSender` still set, and the Stop using confirm says "Emails will use this server's settings" → AC-4, AC-5, value sourcing (`source`, `installSender`, confirm sentence)
- [ ] As a console user outside the org → 404 `project_not_found` on get, update, delete, and test; as a viewer → 200 on get and 403 `forbidden` on the other three, also with an invalid body → AC-24
- [ ] Set the project's status to `provisioning` → get still answers 200; update, delete, and test answer 409 `project_not_ready`, but an invalid body still answers 400 first → AC-24
- [ ] Send 31 tests within 15 minutes as one console user → the 31st answers 429 `rate_limited` with `Retry-After`, even when the earlier ones failed the body check → AC-25
- [ ] Start any role with `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=yes`, or `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT=0` → it exits 1 and names the setting; with both empty it starts → AC-28
- [ ] `dotnet run --project dev/Orvano.AppHost` → a `mailpit` resource runs, and `api` and `worker` start with `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=true` → AC-29 (AppHost part)
- [ ] `dotnet run --project server/tests/Orvano.ModelDriftCheck` against an empty bootstrapped database → "EF model matches the database (18 tables checked)" → data model

## Slice 2: the queue, through console invites

Covers AC-7, AC-15 to AC-17, AC-19 to AC-23, AC-26, AC-27, and the compose part of AC-29. Setup as above, signed in as the install admin (the first account). In the Aspire dashboard, note Mailpit's `smtp` port.

### UI / manual

- [ ] Open `/install` → an "Email server" card shows an empty form starting on STARTTLS, and a "Console emails" card says "No emails in the last 30 days" → AC-7, AC-21
- [ ] Fill Host `localhost`, Port (Mailpit's smtp port), Security None, From email `orvano@install.test`, press Send test email → "Sent to <your console email>", and the email in Mailpit says "this Orvano server" → AC-7, value sourcing (test email body)
- [ ] Save → a toast, and "Remove email server" appears; open a project's Email > Settings → "Using this server's email settings, sending as orvano@install.test" → AC-7, AC-4
- [ ] On an org's Members page, invite `new@example.com` as Developer → the link step shows "We're sending the invite to new@example.com." above the link → AC-23 (`emailed`)
- [ ] In Mailpit → the email's subject is "<your name> invited you to join <org> on Orvano", it names the role, its "Accept invite" button opens the same link the dialog showed, and the expiry reads like `Oct 7, 2026, 07:05 UTC` → AC-23, value sourcing (invite email, expiry text)
- [ ] Back on `/install` → Console emails lists Console invite, `n***@example.com`, Sent, 1 attempt, with Created and Completed in your own time zone (change the system time zone and reload: the times move, the row does not) → AC-21, AC-22, value sourcing (log times)
- [ ] Open the project's Email > Log as a viewer → the tab loads, shows "No emails in the last 30 days", and the title is `Email log · <project> · Orvano` → AC-20
- [ ] Press "Remove email server" → the confirm says "Projects without their own settings won't be able to send email.", focus starts on Cancel; confirm, then invite someone → no "We're sending" line → AC-7, AC-23
- [ ] Sign in as an account that is not an install admin and open `/install` → the not found screen → AC-7

### Commands

- [x] `dotnet test --solution Orvano.slnx` → all pass; `EmailQueueTests`, `InstallEmailApiTests`, and `EmailQueueDomainTests` cover the queue against Postgres and Mailpit → AC-7, AC-15 to AC-17, AC-19 to AC-23, AC-26, AC-27 · checked 2026-09-30: 1,100 of 1,100 passed
- [ ] After an invite is sent: `SELECT payload FROM orvano.jobs WHERE kind = 'messaging.email.send'` holds only `emailId`; `SELECT status, recipient_masked, content_ciphertext, smtp_source FROM orvano.messaging_emails` shows `sent`, the masked address, null content, `install`; the `messaging.email.sent` event holds `projectId`, `emailId`, and a `system` actor only; `grep` the api and worker logs for the invited address and the link token → nothing → AC-15, AC-22, AC-26, value sourcing (email ID, `recipient_masked`)
- [ ] Point the install SMTP at a port nothing listens on, invite someone, and watch `SELECT attempts, run_at - now() FROM orvano.jobs WHERE kind = 'messaging.email.send'` → attempt 1 waits about 30 seconds, then 1, 2, 4, and 8 minutes; fix the port while it waits → the next attempt sends it → AC-15, AC-16, value sourcing (retry delay, SMTP settings at attempt time)
- [ ] Leave the port wrong for 6 attempts → the row ends `failed` with `smtp_unreachable`, the job ends `succeeded`, and the Log's Reason reads "Couldn't connect to the SMTP server." → AC-16, value sourcing (failure code)
- [ ] Queue an invite with the worker stopped, run `UPDATE orvano.messaging_emails SET created_at = now() - interval '31 minutes'`, start the worker → the row fails with `email_expired` and nothing arrives → AC-17, value sourcing (stale check)
- [ ] Start `api` with `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT=2` and send three invites → the third is created with `emailed: false` and only two rows exist → AC-19, AC-23, value sourcing (the cap's count and the limit)
- [ ] As an account that is not an install admin: `GET`, `PUT`, `DELETE /v1/console/install/smtp`, `POST .../smtp/test`, and `GET /v1/console/install/emails` → 403 `forbidden` each; the 31st `testSmtp` in 15 minutes as the admin → 429 → AC-7, AC-21, AC-25
- [ ] `GET /v1/console/project/emails?limit=2` on a project with 5 rows → newest first with a `nextCursor`; follow it to the end; `?cursor=nope` → 400 `invalid_cursor`; `?limit=0` → 400 → AC-20
- [ ] Delete a project with grace days 0 and let the worker run → its rows are gone from `messaging_emails`, `messaging_email_templates`, and `messaging_smtp_settings` → AC-27
- [x] `docker compose -f tests/scenarios/compose.yml up -d --build --wait` → a `mailpit` service is healthy, `GET /v1/console/install/smtp` as the fixture admin shows host `mailpit`, and an invite created there appears at `http://localhost:8025` → AC-29 (compose part). Not yet run locally: Docker Hub pulls hung on the build machine. · checked 2026-09-30: run on a fresh rebuild: `mailpit` healthy, and `e2e/members.spec.ts` read the invite from `http://localhost:8025`, delivered through the fixture's install SMTP

## Slice 3: templates and the auth queue

Covers AC-8 to AC-14 and AC-18. Setup as above, with SMTP pointing at Mailpit (the project's own, or the install's). Sign in as an owner or developer unless a step says otherwise.

### UI / manual

- [ ] Open the project's Email, choose the Templates tab → four rows in this order: Email verification, Password reset, Magic link, Email code, each with its one line description and a Default badge; the title is `Email templates · <project> · Orvano` → AC-8
- [ ] Open Password reset → a Subject input, an HTML editor, a Text editor, the variables `project.name`, `user.email`, `user.name`, `action_url`, `expires_in_minutes` each with a description, a sample, and a copy button, and a preview on the right → AC-9, value sourcing (`subject`, `html`, `text` when not custom, `variables`)
- [ ] Read the samples → `project.name` is this project's real name, `user.email` and `user.name` are your own console email and name, `action_url` is `https://example.com/auth/confirm?token=sample`, `expires_in_minutes` is 60; open Email code → it lists `code` with sample `428613` and 10 minutes, and no `action_url` → value sourcing (sample values)
- [ ] Rename the project to `Tom & <Jerry>` and reload the editor → the preview's Subject shows `Tom & <Jerry>` as typed, the HTML view shows the same text (view the frame's source: `Tom &amp; &lt;Jerry&gt;`), and the Text view shows it as typed → AC-11, value sourcing (`project.name`)
- [ ] Type in the Subject and in the HTML editor, then stop → about half a second later the preview shows the change, and nothing was saved (reload: the old content is back, after the leave prompt) → AC-9
- [ ] Clear the Text editor → its placeholder reads "Leave empty to generate it from the HTML", and the preview's Text view shows text made from the HTML, with each link as `text (url)` → AC-9, value sourcing (`text` when the input's is null)
- [ ] Inspect the preview frame → an `iframe` titled "Email preview" with `src="/frames/email-preview.html"` and `sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox"`, holding an `iframe` titled "Email content" with `srcdoc` and `sandbox="allow-popups allow-popups-to-escape-sandbox"` → AC-9, AC-30, AC-31, AC-33 (slice 4; until then the old single `srcdoc` frame)
- [ ] Type `{{ action_ur }}` on line 4 of the HTML → under the HTML editor: "Line 4: unknown variable action_ur", and the preview says it is waiting; fix it → the error goes and the preview returns → AC-10
- [ ] Try each in the HTML and read the error: `{{ user.emial }}`, `{{ user.name | raw }}`, `{% include 'x' %}`, `{% render 'x' %}`, `{% if %}`, `{% for i in (1..100001) %}x{% endfor %}` → unknown variable, unknown filter, include is not allowed, render is not allowed, a parse error with its line, and "rendering takes more than 100,000 steps" → AC-10
- [ ] Use `{% assign greeting = "Hi" %}{{ greeting }}` and `{% for n in (1..2) %}{{ n }}{{ forloop.index }}{% endfor %}` → both preview with no error → AC-10 (names the template creates)
- [ ] Change the subject to `Reset it, {{ project.name }}` and press Send test → "Sent to <your console email>. Check your inbox.", and the email in Mailpit has the edited subject with the project name, an HTML part, and a text part → AC-12, value sourcing (any test send `sentTo`)
- [ ] Press Save → a "Template saved" toast, the badge turns to Custom with "Edited just now", Save goes back to "Nothing to save yet", and the Templates tab shows Custom with the time on that row → AC-8, AC-9, value sourcing (`isCustom`, `updatedAt`)
- [ ] Change something, then choose "All templates" → a dialog asks "Leave without saving?" with focus on Cancel; Cancel keeps you and your text; "Leave without saving" leaves. Also try closing the tab → the browser asks first → AC-9
- [ ] Press "Reset to default" → the confirm starts on Cancel; confirm → the editors show the default again, the badge reads Default, and the button is gone → AC-9
- [ ] In two browsers, save different subjects for the same template one after the other → the second one is what a reload shows in both → AC-9 (last write wins)
- [ ] Sign in as a viewer and open a template → the same page with read only editors and the variable list, a note that editing is for developers and owners, and no preview, Save, Reset to default, or Send test → AC-9, AC-24
- [ ] Remove every SMTP server (the project's and the install's), then press Send test → "No email server is set up" in the form's alert → AC-12 (`email_not_configured`)
- [ ] Click into the HTML editor, press Tab → the line indents and focus stays; press Escape, then Tab → focus moves to the Text editor → AC-30
- [ ] Open `/projects/<id>/email/templates/console_invitation` → "No such template", with the link back to all templates → API surface (unknown `kind`)
- [ ] Open a default template's preview and read it: one heading, a greeting, a button at least 44 px tall (or the code at 28 px in a monospace font), "This link works for 60 minutes and can be used once.", and "If you didn't ask for this, you can ignore this email." → AC-13

### Commands

- [x] `dotnet test --solution Orvano.slnx` → all pass; `EmailTemplateDomainTests` (the rules, the limits, the encoding of every filter, the defaults), `EmailTemplateApiTests` (the operations over HTTP with Mailpit), and the auth email tests in `EmailQueueTests` cover the slice → AC-8 to AC-14, AC-18 · checked 2026-09-30: 1,100 of 1,100 passed
- [x] `pnpm --filter @orvano/console test` → all pass; `email-templates.browser.test.tsx` covers the tab, the editor, the prompts, the keyboard, and axe in both themes and densities → AC-8, AC-9, AC-30 · checked 2026-09-30: 613 of 613 passed
- [ ] As a viewer: `PUT`, `DELETE`, `POST .../preview`, and `POST .../test` on `/v1/console/project/email/templates/recovery` → 403 `forbidden` each; the same on `/templates/nope` → 404 `not_found` first; as someone outside the org → 404 `project_not_found` → AC-24
- [ ] `PUT` a subject of 256 characters, then an HTML part of 102,401 bytes → 400 `invalid_request` with `subject: ...` and `html: ...`; `PUT` `{{ action_ur }}` on line 4 → 422 `template_invalid` with `html: line 4: unknown variable action_ur` → AC-10
- [ ] `PUT` an HTML part of `{% assign s = 'ab' %}{% for i in (1..30) %}{% assign s = s | append: s %}{% endfor %}` → 422 "a value or the output grows past 1 MB", answered at once with no memory spike on the api → AC-10, AC-11
- [ ] Send 301 previews within 5 minutes → the 301st answers 429 with `Retry-After`; the 31st test within 15 minutes answers 429 → AC-25
- [ ] Set a project to `provisioning` by hand, then `PUT`, `DELETE`, and `POST .../test` a valid template → 409 `project_not_ready`; `GET` and `POST .../preview` still answer 200 → AC-24
- [ ] After a save and a reset: `SELECT type, subject, payload FROM orvano.events WHERE type LIKE 'messaging.template.%'` → `messaging.template.updated` and `messaging.template.reset`, each with `projectId`, `kind`, and the actor, the update with `changed`, and no subject or body text → AC-26
- [ ] From a test or a scratch endpoint, call `IEmailQueue.QueueAuthEmailAsync` inside a transaction and roll it back → no row in `orvano.messaging_emails` and no job; commit one → one `queued` row whose `template` is the kind, one `messaging.email.send` job with 6 attempts on queue `messaging`, and the worker delivers it with `Message-ID: <emailId@public host>` → AC-14, value sourcing (email ID, `Message-ID` host, `recipient_masked`)
- [ ] Queue with no SMTP anywhere → `NotConfigured` and nothing written; with `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT=1` and only the install's SMTP, queue twice → the second is `RateLimited` with a `RetryAfter` just under an hour; give the project its own SMTP → it queues again → AC-14, AC-19, value sourcing (effective SMTP, the cap's count and `retryAfter`)
- [ ] Queue with an `http` action url while `ORVANO_PUBLIC_URL` is `https` → `ArgumentException`; with a code of 3 characters → `ArgumentException`; nothing is written either time → AC-14
- [ ] Save a custom verification template whose subject is `{{ user.name }}`, then queue a verification email for a user with no name → the email arrives with the default subject, and the api log has one warning that names the email ID and `verification` and no address → AC-18, value sourcing (the rendered email)

### Known gap

- [ ] Open the editor on the production shape (`docker compose -f tests/scenarios/compose.yml --profile console up -d --build --wait`, then `http://localhost:8081`) → the two editors look right and the page reports no Content Security Policy violation. The preview frame, however, inherits the console's policy: the browser blocks every inline style inside it (13 for the default password reset template, logged in the frame, not the page), so the preview shows the hidden preheader, a plain blue link for the button, and default fonts. It looks right under the AppHost, which sends no policy. Decided on 2026-09-30: the preview gets its own frame page and policy (AC-31 to AC-33), built as slice 4; this step then expects the styles to show. `e2e/email-templates.spec.ts` passed on this stack, with the other 26 end to end tests, on a fresh database.

## Slice 4: the preview frame · updated 2026-09-30

### UI / manual
- [ ] On `http://localhost:8081` (the gateway), open the password reset template → its preview shows the button dark and 44 px tall, the preheader hidden, and the browser console shows no Content Security Policy message from any frame → AC-31, AC-32 (this closes the known gap above)
- [ ] Inspect the preview → an outer `iframe` titled "Email preview" with `src="/frames/email-preview.html"` and `sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox"`; inside it, `<html lang="en">`, a white page, and an `iframe` titled "Email content" with `srcdoc` and `sandbox="allow-popups allow-popups-to-escape-sandbox"` → AC-30, AC-31
- [ ] Paste HTML with a `<style>` rule, a `data:` image, an `https:` image, a remote stylesheet, a `<script>`, an `onclick`, an `https` link, a `javascript:` link, a relative link, and `<meta name="referrer" content="unsafe-url">` → the style and both images apply (the `https:` image request has no `Referer`); the stylesheet is blocked; the script and handler never run; the `https` link opens a new tab with no opener; the `javascript:` and relative links have no `href` and do nothing; the meta tag is gone → AC-31, AC-32, value sourcing (where links open, the referrer images send)
- [ ] Edit only the subject, then make the subject invalid → the frame page loads once (Network tab), the last preview stays dimmed through the 422, and fixing it brings the preview back without a reload → AC-33
- [ ] Switch the preview to Text and back → the frame is not reloaded and no image is fetched again → AC-33
- [ ] Under `pnpm --filter @orvano/console dev` (or the AppHost) and `vite preview`, the preview looks the same as behind the gateway → AC-32

### Commands
- [ ] `curl -sI http://localhost:8081/frames/email-preview.html` → exactly the headers in `deploy/gateway/email-preview-headers.caddy`; `curl -sI http://localhost:8081/Frames/email-preview.html` → the console's headers (`frame-ancestors 'none'`, `X-Frame-Options: DENY`) → AC-32
- [ ] The same two `curl` calls against the Vite dev server and `vite preview` → the same answers → AC-32
- [ ] `pnpm --filter @orvano/console exec vitest run --project unit src/email` → the hash test passes; change one byte of the frame page's script → it fails and names the new hash → AC-32, value sourcing (the `script-src` hash)
- [ ] `docker build -f deploy/gateway/Dockerfile .` → the `caddy validate` step passes → AC-32
- [x] `pnpm --filter @orvano/console test` → the handshake tests pass (ready answered with the latest HTML, foreign sources and shapes ignored, no post for a subject only change, a reload gets the HTML again, the 5 second alert) → AC-33 · checked 2026-09-30: 613 of 613 passed
- [x] `pnpm --filter @orvano/console test:e2e` with the `console` compose profile up → `headers.spec.ts` and `email-templates.spec.ts` pass → AC-31, AC-32, AC-33 · checked 2026-09-30: 31 of 31 end to end tests passed on a fresh rebuild

## Slice 5: quality and end to end · updated 2026-09-30

### UI / manual
- [ ] Open a project's Email entry → it lands on Settings, titled `Email settings · <project> · Orvano`, with Settings current in the tabs; with only the install's SMTP the info line names its sender and never its host → AC-4
- [ ] On an empty form, pick TLS → Port fills with 465; pick None → Port stays 465; clear Port, pick STARTTLS → 587 → Screens
- [ ] Press Send test email on an empty form → Host, Port, and From email each show their error under the field, read out as alerts, and nothing is sent → AC-2, AC-30
- [ ] Each Email tab (Settings, Templates list, editor, Log) and `/install` in dark and light, compact and comfortable → axe finds nothing, and the Settings tab also passes with an SMTP error showing and as a viewer → AC-30
- [ ] As a viewer on Settings → the fields are read only, there is no Password field, and Save, Send test email, and Stop using these settings are disabled with "Developers and owners only" → AC-24

### Commands
- [x] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "Orvano.Server.Tests.Messaging.EmailQueueTests"` → `A_temporary_rejection_is_retried_and_a_permanent_one_fails_at_once` passes: Mailpit's chaos mode answers 451, the send retries after 30 seconds as `smtp_rejected`, then arrives on attempt 2; a 550 for the recipient fails at once → AC-15, AC-16, value sourcing (the retry decision from the reply code) · checked 2026-09-30: passed
- [x] Same class → `Logs_carry_IDs_and_codes_but_no_address_link_name_subject_or_host` passes: every log line from a queue, a fallback, a 451 retry, a send, and an unreachable host carries the email ID, project, codes, and 451, and none of the address, link, token, user name, subject, project name, host, or code → AC-18, AC-26 · checked 2026-09-30: passed
- [x] `dotnet test --project server/tests/Orvano.Server.Tests --filter-class "Orvano.Server.Tests.Messaging.SmtpSettingsApiTests"` → `A_refused_test_is_not_retried_and_answers_the_reply_code` passes: a 451 and a 550 each answer 502 `smtp_rejected` with the code in `detail`, nothing arrives, nothing is stored, and the api output has no sender or subject → AC-6, AC-26 · checked 2026-09-30: passed
- [x] `pnpm --filter @orvano/console exec vitest run --project browser email-settings` → the Settings tab tests pass (sources, port filling, save, test, field and SMTP errors, Stop using with both confirms, the viewer, axe in four modes) → AC-1 to AC-6, AC-24, AC-30 · checked 2026-09-30: 12 of 12 passed
- [x] With the `console` compose profile up, `pnpm --filter @orvano/console test:e2e` → `email-settings.spec.ts` passes (project SMTP pointed at Mailpit, a refused From email shown under its field, an unreachable port shown in the alert, a test email that arrives from the new sender to the caller, save across a reload, Stop using) and `members.spec.ts` passes (the link step says the invite is being sent, the same link arrives in Mailpit with the right subject, and `/install` Console emails shows it Sent as `e***@example.com`) → AC-1, AC-2, AC-4 to AC-6, AC-21 to AC-23 · checked 2026-09-30: 31 of 31 passed on a fresh rebuild
- [x] Run `test:e2e` twice in a row within 15 minutes → expect the shell sign in test to hit the console sign in limit (10 per 15 minutes); that is the limit working, not a failure of this slice · checked 2026-09-30: observed: the second back to back run failed only that test, with "Too many attempts"

## Acceptance-criteria coverage

- AC-1 · save and test steps, the password commands
- AC-2 · the two field error steps, `SmtpSettingsRuleTests`
- AC-3 · the private host command, `GlobalUnicastTests`, `SmtpConnectorTests`
- AC-4 · the empty state step, the install row command
- AC-5 · the Stop using step, the install row command
- AC-6 · the test email steps
- AC-24 · the viewer step, the role and state commands
- AC-25 · the 31 tests command
- AC-28 · the bad setting command
- AC-29 (AppHost part) · the AppHost command
- AC-7 · the `/install` steps, the install admin command
- AC-15, AC-16 · the retry and sixth attempt commands, `EmailQueueTests`
- AC-17 · the stale command, the retention test
- AC-19 · the cap command
- AC-20, AC-21, AC-22 · the Log and Console emails steps, the paging command
- AC-23 · the invite steps
- AC-26 · the jobs, events, and logs command
- AC-27 · the purge command
- AC-29 (compose part) · the compose command
- AC-8, AC-9 · the Templates tab and editor steps, `email-templates.browser.test.tsx`
- AC-10, AC-11 · the error steps, the size and encoding commands, `EmailTemplateDomainTests`
- AC-12 · the Send test steps, `EmailTemplateApiTests`
- AC-13 · the default template step, `EmailTemplateDomainTests`
- AC-14, AC-18 · the queue commands, `EmailQueueTests`
- AC-30 (the editor part) · the keyboard step, the frame step, axe in the browser tests
- AC-31, AC-32, AC-33 · the slice 4 steps and commands, `email-preview-page.unit.test.ts`, the handshake tests in `email-templates.browser.test.tsx`, `e2e/headers.spec.ts`, `e2e/email-templates.spec.ts`
- AC-30 (the frame titles) · the slice 4 inspect step
- AC-30 (the rest) · the slice 5 axe and field error steps, `email-settings.browser.test.tsx`, axe in four modes in every Email browser test and `-install.browser.test.tsx`
- AC-1 to AC-6, AC-21 to AC-23 end to end · `e2e/email-settings.spec.ts`, `e2e/members.spec.ts`
- AC-16 (a real 4xx reply), AC-26 (logs) · the slice 5 commands
