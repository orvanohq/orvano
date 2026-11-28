# Orvano console

The Orvano dashboard: a Next.js static export, served by `orvano` at `/console`. It uses only the public API, through the TypeScript SDK. Spec [0003](../docs/specs/0003-console-shell-design-foundation/index.md) designs the shell, the tokens and the base components.

## Run it

The easiest way is the AppHost, which starts Postgres, `orvano`, the executor and this dev server as the `console` resource. Its dashboard link opens `/console/`, and edits show without a restart:

```bash
dotnet run --project server/src/Orvano.AppHost
```

You can also run the console on its own from the repo root:

```bash
pnpm --filter @orvano/console dev     # open http://localhost:3000/console/
```

## Build it

```bash
pnpm --filter @orvano/console build   # writes the static site to console/out
```

To see the build through the host, point `Console:RootPath` at it and run `orvano`:

```bash
Console__RootPath=../../../console/out dotnet run --project server/src/Orvano.Host
```

Or serve it with the test server, which follows the same rules as the host (redirects, not found, headers, caching):

```bash
node console/scripts/serve-export.mjs --port 3100   # open http://localhost:3100/console/
```

## Test it

The Playwright and axe tests run against the built export. Build first, then:

```bash
pnpm --filter @orvano/console exec playwright install chromium   # once
pnpm --filter @orvano/console test:e2e
```

## Tokens and colours

Every colour comes from `docs/design/design-system/tokens.json`. `scripts/tokens.mjs` writes it into `src/styles/tokens.css`, which is committed and never edited by hand. After you change `tokens.json`, run:

```bash
pnpm --filter @orvano/console tokens
```

`pnpm --filter @orvano/console lint` fails when `tokens.css` is stale, and when a raw colour (a hex value, `rgb(…)`, `bg-blue-500`, `text-white` and the like) appears anywhere in `src/` outside `tokens.css`. Use the shadcn names (`bg-primary`, `text-muted-foreground`) or the token names (`bg-surface-subtle`, `text-ink-muted`, `bg-success-subtle`). Text on a `-subtle` background uses `brand-ink` or `ink`, never the strong colour.

## Where things live

| Path                 | What it is                                                                                                                    |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `src/shell/nav.ts`   | Every rail destination: label, title, icon, route, description, milestone. A feature that builds a page changes its own entry |
| `src/shell/`         | Rail, top bar, drawer, theme menu, project guard, `useProjectId` and `projectHref`                                            |
| `src/components/ui/` | shadcn/ui components, adjusted to the tokens                                                                                  |
| `src/components/`    | Our own: code block, status dot, page header, empty state, type to confirm                                                    |
| `src/app/kit/`       | `/console/kit/`, every base component and state. Not linked from the rails; use it to check a component change in both themes |

## Project addresses

Project pages read the project from the query string: `/console/project/database/?project=prj_…`. Build every project link with `projectHref(route, id)` so the project carries forward. A component that reads the id with `useProjectId()` needs a Suspense boundary around it, because it uses `useSearchParams`.

## Route data files

With `trailingSlash: true`, Next writes each page as `<route>/index.html` beside its route data: `<route>/index.txt` and segment files named like `<route>/__next._tree.txt`, `__next._full.txt` and `__next.<segments>.__PAGE__.txt` (route groups appear base64 encoded, for example `__next.!KGluc3RhbmNlKQ.members.__PAGE__.txt`). The client fetches these on navigation, so the host must serve `.txt` files. The navigation test fails if one is missing.
