# Shared scenarios

One list of scenarios, run by every SDK surface against a real Orvano (spec 0001, AC-10). Each
`*.yaml` file here (except `fixtures.yaml`) is one scenario:

```yaml
name: health returns version
requires: []                 # fixture names from fixtures.yaml
steps:
  - op: health.get           # an operationId from the contract
    as: client               # client | server | console: which auth to use
    input: {}                # path and query parameters by name, the JSON body under `body`
    expect:
      status: 200            # or a failure: { status: 409, code: user_already_exists }
      body:                  # a subset match: only the keys you list are checked
        status: ok
    save:
      version: $.version     # later steps can use '${version}' (quote it, YAML reads { } as a map)
```

`${unique}` is built in: twelve random lowercase letters and digits, fresh for each scenario run, so
runs against one server never collide on unique values such as emails. Client and server steps
send the first fixture project as `X-Orvano-Project`, and server steps send that project's first
fixture API key. A browser can't hold an API key, so there a server step whose operation needs a
scope skips the scenario.

These runner operations are not in the contract. Their names have no dot, so they never collide
with an operationId:

| `op` | `as` | Input | Body |
|---|---|---|---|
| `signIn` | `client` | `body: { email, password }` | the sign in answer; the SDK's stored session is left alone, so a runner without client operations (.NET) gets a token too |
| `signInFrom` | `client` | `clientIp`, `body: { email, password }` | `signIn` sent as a trusted app server sends it for the visitor at `clientIp` (`X-Orvano-Client-IP`, spec 0014); the fixture project trusts the scenario network; .NET skips it |
| `redeemLink` | `client` | `url`, optional `password` | `{ type, user, isNewUser }` from the client SDK's link helper (`redeemLink` in JS, `handleLink` in Dart), or null for a URL without Orvano's parameters; .NET skips it |
| `now` | `client` or `server` | none | `{ now }`, the runner's clock as an ISO 8601 time; save it before a send and pass it to `test.getLatestEmail` as `after` |
| `verifyAccessToken` | `server` | `token`, optional `online: true`, optional `requireMfa: true` | `{ userId, sessionId, emailVerified, expiresAt, aal, amr }` from the server SDK's own check, or its `token_expired` / `invalid_token` / `mfa_required` error |
| `accessToken` | `client` | none | `{ token }`, the access token the client SDK stores now, so a server step can verify a session made by a client helper such as `completeMfa`; .NET skips it |
| `totpCode` | `client` | `secret`, optional `offset` (steps from now) | `{ code }`, the code an authenticator app shows for that step (spec 0013) |
| `completeMfa` | `client` | `totpCode`, `recoveryCode`, or `passkey: true` (the runner's test authenticator answers) | `{ user, isNewUser, mfaRequired, factors }` from the client SDK's `completeMfa`, which uses the ticket the last sign in kept (201); .NET skips it |
| `verifyMfa` | `client` | `totpCode`, `recoveryCode`, or `passkey: true` | `{ verified: true, refreshTokenKept }` after the client SDK's `verifyMfa` stored the new access token; Orvano sends no refresh token, so `refreshTokenKept` is true when the client still holds the one it held before (spec 0013) |
| `confirmTotp` | `client` | `code` | `{ recoveryCodes, refreshTokenKept }` from the client SDK's `confirmTotp`, which stores the new access token; `refreshTokenKept` as for `verifyMfa` |
| `registerPasskey` | `client` | optional `name`, optional `password` (the user's current password, spec 0013) | the `Passkey` from the client SDK's `registerPasskey`, whose passkey the server's `Test` only software authenticator makes (201) |
| `oauthSignIn` | `client` | `provider`, `testUser` (the fake provider's user), optional `redirectUrl`, optional `link: true`, and for a link optional `password` | `{ type, ... }` from the client SDK's `signInWithOAuth`, or `linkIdentity` with `link: true` (sending `password`), following the fake provider over HTTP (spec 0012); .NET runs only the sign in |
| `signInWithPasskey` | `client` | none | `{ user, isNewUser, mfaRequired, factors }` from the client SDK's `signInWithPasskey`, signed by the newest passkey the software authenticator made (201) |

SdkGen writes a test only dispatch table per language (`operationId` to the generated method), so
each SDK has one small interpreter instead of one test per scenario. A step whose operation has no
call for that role in an SDK (a `client` operation in the .NET SDK, for example) skips the scenario
on that surface.

## Runners

| Surface | Runner | Run it (server on `ORVANO_ENDPOINT`) |
|---|---|---|
| JS core in Node, Bun, Deno, Chromium, workerd | `runners/js` | `pnpm --filter @orvano/scenarios-js scenarios <node\|bun\|deno\|browser\|workerd>` |
| Next.js | `runners/nextjs` (driven by `runners/js`) | `pnpm --filter @orvano/scenarios-js scenarios nextjs` |
| Dart server | `runners/dart` | `dart run bin/run.dart` (in `runners/dart`) |
| Flutter (iOS, Android, web) | `runners/flutter` | see `runners/flutter/README.md` |
| .NET (`net10.0`, and `netstandard2.0` through `net8.0`) | `runners/dotnet` | `dotnet run --project tests/scenarios/runners/dotnet -f net10.0` |

Start a server for them with `docker compose -f tests/scenarios/compose.yml up -d --build`; it
listens on `http://localhost:8080` in the `Test` environment, where every response is checked
against the contract.

The api keeps its rate limits in memory (spec 0004), and every runner on one machine shares one
IP, for example 60 sign ups an hour. When you run many surfaces against one server in a row and
start getting 429 `rate_limited`, restart it (`docker compose -f tests/scenarios/compose.yml
restart api`). CI gives each surface its own server.
