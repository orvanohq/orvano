# Verify: Transactional email · spec 0009 · updated 2026-09-30

_Steps derived from spec 0009 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones._

This file covers slice 1 of the build plan so far (project SMTP and a test email that arrives): AC-1 to AC-6, AC-24, AC-25, AC-28, and the AppHost part of AC-29. Later slices add their own steps below.

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

- [ ] `dotnet test --solution Orvano.slnx` → all pass; `Orvano.Server.Tests.Messaging` covers the rules and the HTTP path against Mailpit → AC-1 to AC-6, AC-24, AC-25, AC-28
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
- Not built yet: AC-7 to AC-23, AC-26, AC-27, AC-29 (compose part), AC-30
