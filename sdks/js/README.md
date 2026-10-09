# @orvano/js

The Orvano SDK for JavaScript and TypeScript: browsers, Node, edge runtimes, Deno, and Bun.

## Use it

```bash
npm install @orvano/js
```

```ts
import { Client, Orvano } from '@orvano/js'

const orvano = new Orvano(new Client({ endpoint: 'https://orvano.example.com' }))
const { version } = await orvano.health.get()
```

App code imports `@orvano/js`, which has no way to set an API key. Trusted server code imports `@orvano/js/server` and passes `apiKey`.

## Email links and codes

Orvano emails verification, password reset, magic link, and email change links that open your own page, with `orvano_type` and `orvano_token` added to the URL. On that page, one call redeems any of them:

```ts
const result = await orvano.client.redeemLink() // reads location.href in a browser
// { type: 'magic_link', user, isNewUser } or null when the URL is not an Orvano link
```

A magic link or reset signs the user in; pass `{ password }` for a reset. A verification or email change refreshes the session when the same user is signed in, so the access token's `email_verified` claim is current. After a success the two parameters are removed from the address bar. For a code, call `orvano.client.signInWithEmailCode(email, code)`.

Make the landing page safe for links:

- Show a Continue button that redeems the link, instead of redeeming on page load. Mail scanners open links to check them, and each link works only once.
- Send `Referrer-Policy: no-referrer` on that page, so the token never leaks to another site in a `Referer` header.

Every failure is an `OrvanoError`; a limit's refusal carries `retryAfter`, in seconds.

## Sign in with a provider

Google, Apple, GitHub, and Microsoft, once the project turns them on in the console:

```ts
await orvano.client.signInWithOAuth('google', { redirectUrl: `${location.origin}/auth/callback.html` })
// On the callback page, the same redeemLink() finishes it (orvano_type=oauth):
const result = await orvano.client.redeemLink()
```

The PKCE verifier waits in `sessionStorage` until the user comes back, so only this browser can finish. `linkIdentity` adds a provider to the signed in user; a user with a password passes it as `password`. A native wrapper that gets a Google or Apple ID token itself calls `signInWithIdToken` with a nonce from `createNonce()`. Guides: https://orvano.dev/docs/auth/sign-in-with-google/

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview and only covers the health check so far.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
