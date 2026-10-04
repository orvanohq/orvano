# Protocol baseline - endpoints, grants, token lifecycle

The concrete OAuth 2.1-profile checklist behind SKILL.md step 1. Spec-status precision, repeated because it gets misquoted: RFC 9700 (BCP 240, January 2025) is final; the consolidated OAuth 2.1 document was at draft-ietf-oauth-v2-1-15 (March 2026) - settled convergence, not yet a ratified RFC.

## Endpoint checklist

- `/authorize` - authorization endpoint.
- `/token` - token endpoint.
- `/revoke` - RFC 7009 token revocation.
- `/introspect` - RFC 7662 token introspection, for your own resource servers.
- `/.well-known/oauth-authorization-server` - RFC 8414 discovery document. Publish it from day one; it is also where later capabilities (DPoP, PAR) get advertised.
- PAR endpoint (RFC 9126) - only if targeting FAPI-adjacent sensitivity later; zero of 18 surveyed mainstream issuers advertised one (MojoAuth issuer survey).

## Grant checklist

- `authorization_code` - with PKCE S256, enforced for every client, public and confidential.
- `client_credentials` - machine-to-machine.
- `refresh_token`.
- `device_authorization` (RFC 8628) - CLIs, TVs, headless clients.
- Never: `implicit`, resource-owner password credentials - removed under OAuth 2.1.

Client vocabulary worth adopting: alongside public/confidential, Aaron Parecki's (OAuth 2.1 co-editor) **"credentialed client"** - a client holding credentials that prove it is the same client that registered, without proving who its operator is. Client authentication buys identity continuity; real-world identity is what the verification program (SKILL.md step 3) buys.

## Token-TTL benchmark table

All figures from the named provider's own documentation:

| Provider                  | Access token TTL             | Refresh token TTL               | Rotation on use?             |
| ------------------------- | ---------------------------- | ------------------------------- | ---------------------------- |
| GitHub App installation   | 1 hour                       | N/A (installation-scoped)       | Regenerated                  |
| GitHub App user-to-server | 8 hours                      | 6 months                        | Yes - new refresh each use   |
| Slack (rotation enabled)  | 12 hours                     | until rotated                   | Yes, old revoked             |
| Microsoft Entra           | 60-90 min (variable)         | 90-day inactivity; 24h for SPAs | Replaces, doesn't revoke old |
| Salesforce                | ~2 hours (session-dependent) | policy-defined                  | Moving to mandatory rotation |
| Google                    | ~1 hour                      | until revoked or capped         | No - reused until revoked    |

Reading the table:

- **The convergence point is ~1-hour access tokens.** Start there rather than inventing a figure.
- **Slack's default posture is the risky one**: "Without token rotation, the access token never expires. With token rotation, it expires every 12 hours" (Slack docs) - the hardened configuration is opt-in, a shape to avoid copying: make rotation the default, not the upgrade.
- **Google is the deliberate outlier**: no rotation, risk bounded instead by a cap of 100 refresh tokens per account per client. The oldest is silently invalidated at the cap (current Google docs - older cached sources say 50; the number already moved once, re-verify at implementation time).
- **Microsoft compensates differently**: no hard rotation-invalidation, but Continuous Access Evaluation for near-real-time revocation - a different tool solving the problem rotation solves elsewhere (Microsoft Learn).
- **Salesforce's figures are a snapshot, not a target** - actively changing under the May 2026 mandatory-OAuth-controls rollout; re-verify against Salesforce's ISV communications.
- GitHub layers a live-token cap on top of TTLs: more than 10 tokens for the same user/scope pair auto-revokes the oldest (GitHub docs) - a per-app population cap, not a per-token expiry, worth copying.

## Rotation mechanics

- **Token families**: every refresh token belongs to a family traceable to the original grant. Use invalidates the presented token and issues a successor in the same family. A consumed token presented again (outside the grace window) revokes the entire family - the convergent Auth0/Keycloak/Django OAuth Toolkit pattern; Auth0: reuse detection "immediately invalidates the refresh token family".
- **Why family-wide, not single-token**: the attacker may hold an earlier or later token in the same chain than the one that tripped the wire.
- **RFC 9700 §4.14's own framing**: the server "cannot determine which party submitted the invalid refresh token, but it will revoke the active refresh token" - a tripwire, never an arbitration mechanism.
- **Grace window, from day one**: multi-tab SPAs, mobile reconnects, and ordinary retries legitimately re-present a just-consumed token. Both Django OAuth Toolkit (`REFRESH_TOKEN_GRACE_PERIOD_SECONDS`) and Keycloak ("Refresh Token Max Reuse") expose the knob. Starting value ~10-30 seconds - long enough for concurrency races, short enough to still catch replay fast.
- **Design the family relationship into the first grant** - retrofitting family grouping onto an already-issued ungrouped token population is materially harder than starting with it.

## Storage tensions

- Store all tokens hashed at rest (SHA-256) - same rationale as high-entropy API keys.
- **The documented tension**: a grace window must recognize a _prior_ token if re-presented within the window, which pure hash-at-rest storage complicates. Django OAuth Toolkit's docs name this explicitly as a tradeoff to resolve deliberately (e.g. retain the predecessor's hash with a validity timestamp) - not an oversight to "fix" by dropping hashing.
- Opaque tokens revoke instantly via the database; JWTs don't. If issuing JWT access tokens, the short TTL _is_ the revocation story - another reason not to stretch past ~1 hour.

## The compliance-gate pattern

Django OAuth Toolkit 3.4 models RFC 9700 compliance as separately named boolean gates (`COMPLIANT_BCP_RFC9700_*`) - one per BCP requirement (PKCE enforcement, exact redirect matching, rotation, ...) - rather than one "OAuth 2.1 mode" flag. Copy the shape: independently toggleable, independently auditable gates let a partial rollout or an audit point at exactly which requirements are and aren't enforced yet.
