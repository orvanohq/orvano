# Orvano.Messaging

## Overview

The Messaging module (spec 0009): SMTP settings per project with the install's as the fallback, the test email, the four editable auth email templates (Liquid, through Fluid), the sealed send queue and its worker job, console invite emails, and the email log. It reaches Platform only through `Orvano.Platform.Contracts` and never references Auth: Auth queues its emails through `IEmailQueue`.

## Layout

| Folder | Owns |
|---|---|
| `Contracts/EmailQueue.cs` | The only public types: `IEmailQueue` (api role only), `AuthEmail`, `AuthEmailKind`, `EmailQueueResult` |
| `Domain/` | Plain rules: `LiquidEngine` (Fluid with its limits and allowed filters), `DefaultTemplates`, `EmailTemplateCatalog`, `GlobalUnicast` and `SmtpHost` (which hosts a project may use), `EmailAddress`, `RecipientMask`, `HtmlToText`, `MessagingSettings` |
| `Application/` | Use cases (`SmtpSettingsService`, `EmailTemplateService`, `EmailLogService`), `EmailQueue` (the shared queue core, raw Npgsql on the caller's transaction), `AuthEmailQueue`, `InvitationMailer` (Platform's `IConsoleInvitationMailer`), `EmailSealer`, `EffectiveSmtp`, `ProjectAccess`, `MessagingStore`, `MessagingEvents` |
| `Data/MessagingDbContext.cs` | EF Core mapping of the `messaging_` tables (`0005_messaging.sql`); internal, listed in the drift check |
| `Endpoints/` | Thin console endpoints for SMTP, the test email, the log, and templates, plus `ApiMapping` |
| `Smtp/` | `SmtpConnector` (resolves and dials the host itself) and `SmtpSender` (MailKit) |
| `Jobs/` | `messaging.email.send`, `messaging.project.purge` (queued by the `messaging.purge_project` consumer of `platform.project.purged`), and the hourly `messaging.retention` schedule, all on queue `messaging` |
| `Fixtures/MessagingFixtures.cs` | `Test` only: seeds the install SMTP pointing at the scenarios' Mailpit |

## Conventions

- Writes go through `MessagingStore.WriteAsync` (one transaction, commits only on success) and return `Outcome<T>`, the same pattern as Platform. `EmailQueue` is the exception: it writes with raw Npgsql on the caller's transaction, so the email row and its job exist only if the caller's change commits.
- Every project operation starts with `ProjectAccess`: a non member gets 404 `project_not_found`, a viewer gets 403 on a change.
- A queued email's content (recipient, subject, bodies, `Message-ID`) is sealed with `SecretBox` to its row (`EmailSealer`). The send job's payload is the email ID alone. After delivery only `RecipientMask` (`g***@example.com`) remains. Never log a recipient, link, code, or rendered body.
- The send job never renders. It unseals and sends through the SMTP in effect at that moment, and every outcome is one conditional update on `status = 'queued'`, so a duplicate run can't flip a final row. It throws only to ask for a retry (`JobRetryException` sets the delay).
- A project's SMTP host must be global unicast unless `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS=true`. `SmtpConnector` dials the exact addresses it checked, so DNS can't change between the check and the connect.
- Templates render only through `LiquidEngine`: no file access, an HTML encoder on every output, no `raw` filter, and caps on steps, characters, and filter work.
- `ORVANO_SMTP_ALLOW_PRIVATE_HOSTS` and `ORVANO_EMAIL_INSTALL_HOURLY_LIMIT` (the soft hourly cap on install SMTP per project) are read in `ConfigureServices`, so every role refuses a bad value.
- Services that need `SecretBox` or `PublicUrl` register in `ConfigureApiServices`; the worker side only needs the sender and the store.

## Tests

`server/tests/Orvano.Server.Tests/Messaging/`: domain unit tests, queue tests on real Postgres, HTTP tests against the real binary, and `MailpitFixture` for real delivery.

## Related specs

- [0009 Transactional email](../../../docs/specs/0009-transactional-email/index.md) (with `verify.md`)

## Agent skills

- [email-best-practices](../../../.claude/skills/email-best-practices/): `resend/email-best-practices`, deliverability, SPF/DKIM/DMARC, transactional email design and accessibility

Declined: MailKit and Fluid Agent Skills (none found), MCP `lod-softworks/mcp-email-management`

_Drafted by /sync from the introducing change, worth a quick human pass._
