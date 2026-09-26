# @orvano/nextjs

Orvano for Next.js: server components, route handlers, server actions, and middleware. It wraps `@orvano/js` and keeps the user's session in a cookie.

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

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview and only covers the health check so far.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
