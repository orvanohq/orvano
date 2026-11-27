# Scope: Docs

The docs site in `website/`, the machine readable outputs for agents, and the landing page. Every feature adds its own docs page as it ships; these features build the site around those pages. Part of the [Orvano scope](index.md).

## M0 (Phase 1: Foundation)

### 6. Docs site skeleton
A thin docs site that builds in CI, so every later feature has a place to put its page.
**Done when:** `website/` builds a static docs site in CI, themed with the design tokens, with a home page and one sample page whose code sample is compiled by CI.
- [ ] Build it: `/develop docs site skeleton`

## M9 (Phase 3: Launch)

### 90. Docs site complete · needs a decision
The full public docs: structure, search and a generated API reference.
**Done when:** the docs cover every v1 feature in a clear structure; search works; the API reference is generated from the contract; the site is live at `orvano.dev/docs`.
- [ ] Design it (spec): `/architect docs site complete`

### 91. Machine readable docs · needs a decision
Docs an agent can read whole and trust.
**Done when:** `llms.txt`, `llms-full.txt` and `llms-small.txt` are generated on each docs deploy; every page is also served as Markdown; code samples are compiled and type checked in CI.
- [ ] Design it (spec): `/architect machine readable docs`

### 92. Landing page · needs a decision
The front door for the public beta, on the same static site as the docs.
**Done when:** `orvano.dev` shows one landing page that says what Orvano is, how to install it and how to connect an agent, in the design system's look.
- [ ] Design it (spec): `/architect landing page`

## Deferred
- **Full marketing site**: features, comparison and changelog pages
