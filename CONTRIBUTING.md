# Contributing to Orvano

Thanks for helping. [AGENTS.md](AGENTS.md) lists the commands to install, build, test, and format, and the rules every change follows. This file adds what a pull request needs beyond the code.

## Docs ship with the feature

The docs at [orvano.dev](https://orvano.dev) live in [`website/`](website/), and a feature isn't done until its docs are. In the same pull request:

- **A new or changed public operation** adds or updates the guide or concept page that uses it, under `website/src/content/docs/docs/`. The API reference page is generated from the contract, but the build fails when no guide links to an operation of a new tag.
- **A new error code** adds `website/src/content/error-fixes/<code>.mdx` with a `## Why it happens` and a `## How to fix` section. The build fails without it.
- **A new or changed console screen** updates its page under `website/src/content/docs/docs/console/`. If a screenshot changed, run `pnpm --filter @orvano/website screenshots` against a fresh local stack and commit the new images; nothing checks them for you.
- **A change to a quickstart's steps** changes its example in `examples/`. The pages pull their code from there, and CI runs every example against a local stack.
- **A user visible change** adds a line to [CHANGELOG.md](CHANGELOG.md), which the site renders as its Changelog page.

Check the site before you push:

```bash
pnpm --filter @orvano/contract build
ORVANO_SITE_ENV=preview pnpm --filter @orvano/website build
```

The build fails on a broken internal link or anchor, a missing code region, an error code without a fix page, and a reference tag no guide links to. `pnpm --filter @orvano/website dev` serves the site while you write.

Write the way the existing pages do: plain words, `you`, short sentences, and what the reader should see after each step.
