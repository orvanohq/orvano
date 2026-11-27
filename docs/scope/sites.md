# Scope: Sites

Hosting for static sites and Next.js apps, with previews and custom domains. Part of the [Orvano scope](index.md). Builds and runs reuse the sandbox (feature 54) and the GitHub App (feature 60) in [Functions](functions.md).

## M8 (Phase 3: Launch)

### 81. Static sites & deployments · needs a decision
The thinnest hosting thread: deploy a static site and move the production pointer.
**Done when:** a static site or SPA (with an optional fallback route) deploys from the CLI or a zip upload and is served at its `*.sites.<domain>` address; every deploy is immutable and kept; production is a pointer, so promote and rollback are instant; environment variables can differ between production and preview.
- [ ] Design it (spec): `/architect static sites & deployments`

### 82. Next.js SSR sites · needs a decision
Server rendered Next.js apps, run in a container on the executor.
**Done when:** a Next.js app builds in the sandbox and runs from its standalone output in a container on the executor, with the same limits and isolation as functions.
- [ ] Design it (spec): `/architect next.js ssr sites`

### 83. Custom domains & HTTPS · needs a decision
Your own domain on a site or function, with certificates issued for you.
**Done when:** a custom domain added to a site or function gets a certificate on demand, and only for domains registered in Orvano (checked through the `ask` endpoint); the wildcard preview domain gets its certificate through a DNS challenge; hosted sites cannot read or set console cookies.
- [ ] Design it (spec): `/architect custom domains & https`

### 84. GitHub deploys & preview URLs · needs a decision
Deploy on push, and give every branch and pull request its own address.
**Done when:** a linked repo deploys on push; every branch and pull request deploy gets its own preview URL, posted back to the pull request as a comment.
- [ ] Design it (spec): `/architect github deploys & preview urls`

### 85. Sites console & agent tools · needs a decision
The console pages for sites, plus the CLI commands and MCP tools that waited for this milestone.
**Done when:** the console covers deployments, preview links, build logs, domains, environment variables, promote and rollback; `orvano sites deploy|list|promote|rollback` and the MCP tools `deploy_site` and `get_deployment_status` work.
- [ ] Design it (spec): `/architect sites console & agent tools`

## Deferred
- **Edge cache and CDN**: sites served from an edge network
- **Image optimization**: resized images for sites
- **More SSR frameworks**: Astro, SvelteKit, Nuxt
