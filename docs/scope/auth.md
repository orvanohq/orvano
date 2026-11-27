# Scope: Auth

Sign in for the end users of apps built on Orvano. Every feature here is Beta, because a mistake is a security hole. Part of the [Orvano scope](index.md).

## M2 (Phase 1: Foundation)

### 18. Sessions & tokens · needs a decision · Beta
The session model every sign in method ends in: a short lived access token plus a rotating refresh token.
**Done when:** sign in returns a 15 minute access JWT signed with a per project key (with rotation and a JWKS endpoint) and a rotating refresh token stored hashed; a reused refresh token is refused; sign out and sign out of all devices work; web sessions use `httpOnly` cookies.
- [ ] Design it (spec): `/architect sessions & tokens`

### 20. Email + password sign in · needs a decision · Beta
The first real sign in method, and the thinnest thread through auth: sign up, verify, sign in, reset.
**Done when:** a user can sign up, verify their email, sign in and reset a password; passwords need at least 8 characters, are hashed with Argon2id and are checked against breached passwords by default.
- [ ] Design it (spec): `/architect email + password sign in`

### 23. Magic link & email passcode · needs a decision · Beta
Sign in without a password, from an emailed link or a one time passcode.
**Done when:** a user can sign in from an emailed link or a passcode; links and codes expire and work only once.
- [ ] Design it (spec): `/architect magic link & email passcode`

### 24. Google & Apple sign in · needs a decision · Beta
Sign in with Google or Apple, with account linking by verified email.
**Done when:** a user can sign in with Google or Apple through OAuth with PKCE; redirects are limited to registered platforms; a sign in whose verified email matches an existing account joins that account instead of creating a duplicate; an unverified email never links.
- [ ] Design it (spec): `/architect google & apple sign in`

### 27. Auth abuse protection · needs a decision · Beta
Rate limits on the endpoints attackers hammer, and the settings page that controls them.
**Done when:** sign in, sign up, magic link and passcode sends are limited per IP and per email, answering `429` with `Retry-After`; the console security settings page shows and changes the limits and password rules.
- [ ] Design it (spec): `/architect auth abuse protection`

### 28. User management & labels · needs a decision · Beta
The console pages for a project's users, plus the labels that permissions later match on.
**Done when:** the console Users page lists and searches users, opens a user, blocks and unblocks, deletes, and revokes sessions; labels can be set by server keys or the console and appear in the user's claims; the sign in methods page turns methods on and off and holds the Google and Apple setup.
- [ ] Design it (spec): `/architect user management & labels`

## Deferred
- **Teams and roles**: groups of end users as a permission principal
- **MFA**: a second factor with TOTP
- **Passkeys**: sign in with a device credential
- **Phone passcode**: sign in by SMS
- **More OAuth providers**: beyond Google and Apple
- **SSO**: enterprise sign in
- **Anonymous users**: sessions before sign up
- **Sign up hooks**: custom code that runs during sign up
