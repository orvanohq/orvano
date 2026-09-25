# Epic: Authentication

App user identity for projects built on Orvano. See [index.md](index.md) for the full plan.

### 8. App user sign up & sign in · needs a decision · GA
The thinnest real auth thread: email and password sign up, sign in, sign out, and "get current user". Decides the session model (tokens, refresh, and cookie sessions for Next.js server rendering). Together with row 7 this is v0.1.
**Done when:** a Next.js app and a Flutter app sign a user up and in; a .NET and a Dart server verify that user's session and list users; the user shows in the console Users page.
- [ ] Design it (spec): `/architect app user sign up & sign in`

### 9. Transactional email · needs a decision
Send email from a project through SMTP you configure, with editable templates. Auth uses it first; messaging reuses it later.
**Done when:** you can set SMTP per project, edit a template in the console, and send a test email that arrives.
- [ ] Design it (spec): `/architect transactional email`

### 10. Email verification, recovery & passwordless · needs a decision · GA
Verify email, reset password, magic link, and email one time code sign in.
**Done when:** each flow works from every client SDK, links and codes expire and work only once, and the console shows a user's verified state.
- [ ] Design it (spec): `/architect email verification, recovery & passwordless`

### 12. OAuth & ID token sign in · needs a decision · GA
Sign in with Google, Apple, GitHub, and Microsoft through a browser redirect, plus native ID token sign in for mobile (Flutter Google and Apple sign in without a web view).
**Done when:** you enable a provider in the console, and users sign in through it from Next.js (redirect) and Flutter (native); identities link to one user.
- [ ] Design it (spec): `/architect OAuth & ID token sign in`

### 13. MFA, passkeys & sessions · needs a decision · GA
Authenticator app codes, recovery codes, passkeys, and letting users see and revoke their sessions and devices.
**Done when:** a user can enroll a second factor or a passkey, must pass it at sign in, and can revoke any other session from the app.
- [ ] Design it (spec): `/architect MFA, passkeys & sessions`

### 14. Auth policies & abuse protection · GA
Password rules, sign up email policies (block disposable domains), anonymous guest users, session limits, and rate limits on auth endpoints.
**Done when:** each policy is set per project in the console and enforced by the API; repeated failed sign ins are throttled.
- [ ] Build it: `/develop auth policies & abuse protection`
