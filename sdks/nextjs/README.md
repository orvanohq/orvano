# @orvano/nextjs

Orvano for Next.js: server components, route handlers, server actions, and middleware. It wraps `@orvano/js` and keeps the user's session in two cookies: `orvano_access` (the access token) and `orvano_refresh` (the refresh token, `HttpOnly`).

## Use it

```bash
npm install @orvano/nextjs
```

```tsx
// app/page.tsx
import { cookies } from 'next/headers'
import { createServerClient } from '@orvano/nextjs'

export default async function Page() {
  const orvano = createServerClient({ endpoint: process.env.ORVANO_ENDPOINT!, cookies: await cookies() })
  const { version } = await orvano.health.get()
  return <p>Orvano {version}</p>
}
```

Keep the session fresh in middleware, and mount the route handler the browser client refreshes and signs out through:

```ts
// middleware.ts
import type { NextRequest } from 'next/server'
import { updateSession } from '@orvano/nextjs/server'

export function middleware(request: NextRequest) {
  return updateSession(request, { endpoint: process.env.ORVANO_ENDPOINT!, project: 'my-project' })
}
```

```ts
// app/api/orvano/[...orvano]/route.ts
import { createOrvanoRouteHandler } from '@orvano/nextjs/server'

export const { POST } = createOrvanoRouteHandler({ endpoint: process.env.ORVANO_ENDPOINT!, project: 'my-project' })
```

Sign users in on the server (a server action with `createServerClient`), so both cookies get set. In client components, `createBrowserClient` reads the access cookie and refreshes through that route handler.

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
