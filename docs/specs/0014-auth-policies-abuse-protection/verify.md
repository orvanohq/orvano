# 0014. Auth policies: manual checks against real services and a real app server

The shared scenarios use a fake range endpoint for the breached check and a scenario compose network for the trusted server rule, so they can't prove that the real Have I Been Pwned API, a real Next.js deployment, and real inboxes behave as [index.md](index.md) assumes. Run these by hand on the Netcup test server (see the team's server notes) before the row is marked `done`, and again before each release that touches these paths. Record the date, the Orvano version, and any deviation at the bottom.

## Bundled lists

- [ ] The common password list's source and license allow redistribution in an Apache 2.0 project, and `THIRD_PARTY_NOTICES.md` names both → AC-7
- [ ] The disposable domain list's source and license allow it, `tools/lists/refresh.mjs` refreshes it, and a known disposable domain is on it → AC-7, AC-8

## Breached passwords, real API

- [ ] With the switch on, sign up with `P@ssw0rd2024!` (breached, not on the common list) → 400 `password_breached` → AC-6
- [ ] With outbound HTTPS blocked on the host, the same sign up passes and `orvano.auth.hibp_failures` grows in the Aspire or OTLP metrics → AC-6

## Real app server

- [ ] Deploy the Next.js quickstart on a separate host, list its address on the App servers card, and fail 10 sign ins for one email from a phone on mobile data: the 11th from that phone gets 429, while the same email signs in from a laptop on another network; a request from the phone with a forged `X-Forwarded-For` first value still lands in the phone's bucket → AC-16, AC-17, AC-36
- [ ] Remove the CIDR: both devices now share the server's bucket (documented behavior) → AC-16

## Real inboxes

- [ ] With "require verified email" on, sign up a new address and an existing one: both screens look the same, the new inbox gets the verification email with both links, and the existing inbox gets the sign up attempt alert → AC-12, AC-15
- [ ] The reject link removes the unverified user's password and sessions, and the Users page shows the same user with no password; the inbox owner then signs in by magic link → AC-15

## Results

| Date | Version | Who | Deviations |
|---|---|---|---|
