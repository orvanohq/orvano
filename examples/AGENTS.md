# Examples

## Overview

The finished app of each quickstart on orvano.dev (spec 0011): `nextjs-quickstart`, `js-quickstart` (Vite), `flutter-quickstart`, `dart-quickstart` (a `shelf` server), and `dotnet-quickstart` (a Minimal API). The quickstart pages pull their code from these files, and CI runs every one against a real local stack, so a page and its app can't disagree.

## Commands

```bash
node .github/scripts/check-example-pins.mjs   # every Orvano package pinned to exactly VERSION (AC-14)
```

To run one the way CI does, copy it outside the repo and point it at this checkout's SDKs with `node .github/scripts/use-local-sdks.mjs <copy> <packs folder>`; the `quickstarts` job in `.github/workflows/website.yml` shows the full sequence.

## Conventions

- Each example is exactly what a reader copies: it sits outside every workspace (pnpm, the Dart workspace, `Orvano.slnx`) and inherits no repo settings. `Directory.Build.props` and `Directory.Packages.props` here are empty stop files for .NET.
- Orvano packages are pinned to exactly `VERSION`, never a range. SdkGen stamps the pins on a version bump; the other dependencies are the example's own.
- Code a page shows sits between `#region <name>` and `#endregion <name>` comments. Renaming or removing a region breaks the page's build.
- No secrets in code: the endpoint, project ID, and server API key come from environment variables (`NEXT_PUBLIC_ORVANO_*`, `VITE_ORVANO_*`, `--dart-define`, `ORVANO_ENDPOINT`, `ORVANO_PROJECT`, `ORVANO_API_KEY`).
- Fixed dev ports: Next.js 3000, Vite 5173, Flutter web 5050, Dart 3001, .NET 3002. The iOS bundle ID and Android package name are `dev.orvano.quickstart`, the same constant `website/scripts/screenshots.ts` registers.
- A change to an example's steps updates its quickstart page and README in the same pull request.

## Related specs

- [0011 Docs site and quickstarts](../docs/specs/0011-docs-site-quickstarts/index.md)

_Drafted by /sync from the introducing change, worth a quick human pass._
