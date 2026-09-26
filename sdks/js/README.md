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

## About

Orvano is the open source backend you host yourself: auth, Postgres databases, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.

> Orvano is at the very start. This package is a preview and only covers the health check so far.

The SDK version shares the server's major.minor: `0.0.x` of this package targets Orvano `0.0`. The client warns once when the server it talks to runs another major.minor.

This package is generated from Orvano's API contract in the [orvanohq/orvano](https://github.com/orvanohq/orvano) monorepo. Please open issues and pull requests there; the package's own repository is a read only mirror.

## License

Apache 2.0
