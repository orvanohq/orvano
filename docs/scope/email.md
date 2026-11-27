# Scope: Email

Email over SMTP: first the thin strand auth needs, then templates, topics, scheduling and a delivery log. Part of the [Orvano scope](index.md).

## M2 (Phase 1: Foundation)

### 19. SMTP & auth emails · needs a decision
The thin email strand sign in depends on: one SMTP config for the instance and the four built in auth emails.
**Done when:** the instance SMTP settings can be saved and checked with a send test email button; verification, magic link, passcode and reset emails send through it from built in templates that can be edited in the console; a failed send shows the SMTP error.
- [ ] Design it (spec): `/architect smtp & auth emails`

## M5 (Phase 2: Core services)

### 64. Email sending & topics · needs a decision
Send to users, lists and topics from trusted callers only.
**Done when:** a server key, a function or the console can send to a user, a list of users or a topic; clients cannot send; the SDKs manage the signed in user's topic subscriptions; topic emails carry a working unsubscribe link; a project can override the instance SMTP settings.
- [ ] Design it (spec): `/architect email sending & topics`

### 65. Email templates, scheduling & log · needs a decision
Reusable templates, scheduled sends and a record of what went out, with the console pages for all of it.
**Done when:** templates with `{{variables}}` have a live preview and a test send; an email can be sent now or scheduled; the delivery log shows queued, sent and failed with the SMTP error; the console Email pages cover templates, topics, compose and the log.
- [ ] Design it (spec): `/architect email templates, scheduling & log`

## Deferred
- **Push notifications**: through FCM and APNs
- **SMS**: through a provider such as Twilio
- **Provider APIs with bounce webhooks**: delivery and bounce tracking beyond SMTP
- **In app inbox**: messages shown inside the app
