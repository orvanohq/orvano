# Orvano console

The Orvano dashboard: a Next.js static export, served by `orvano` at `/console`. It uses only the public API, through the TypeScript SDK.

You can run it from the repo root:

```bash
pnpm --filter @orvano/console dev
pnpm --filter @orvano/console build
```

The design system and mockups live in `docs/design/`. Feature 5 (console shell) maps them in.
