// orvano.dev (spec 0011): one static Starlight site. ORVANO_SITE_ENV decides `noindex` and the analytics beacon.
import starlight from '@astrojs/starlight'
import { defineConfig } from 'astro/config'
import starlightLinksValidator from 'starlight-links-validator'
import starlightLlmsTxt from 'starlight-llms-txt'
import starlightOpenAPI, { openAPISidebarGroups } from 'starlight-openapi'

const isDev = process.argv.includes('dev')
const siteEnv = process.env.ORVANO_SITE_ENV ?? (isDev ? 'preview' : undefined)
if (siteEnv !== 'preview' && siteEnv !== 'production') {
  throw new Error('Set ORVANO_SITE_ENV to preview or production before you build the site.')
}

const beaconToken = process.env.PUBLIC_CF_BEACON_TOKEN ?? ''

/** @type {import('@astrojs/starlight/types').StarlightUserConfig['head']} */
const head =
  siteEnv === 'preview'
    ? // Previews are never indexed (AC-24).
      [{ tag: 'meta', attrs: { name: 'robots', content: 'noindex' } }]
    : beaconToken === ''
      ? []
      : // Cloudflare Web Analytics: cookieless, so no consent banner (AC-24).
        [
          {
            tag: 'script',
            attrs: {
              defer: true,
              src: 'https://static.cloudflareinsights.com/beacon.min.js',
              'data-cf-beacon': JSON.stringify({ token: beaconToken }),
            },
          },
        ]

export default defineConfig({
  site: 'https://orvano.dev',
  trailingSlash: 'always',
  vite: {
    build: {
      // Vite inlines small assets as `data:` URLs, which the policy's `font-src 'self'` blocks (AC-23). Fonts stay
      // files; everything else keeps Vite's default.
      assetsInlineLimit: (file) => (/\.woff2?$/.test(file) ? false : undefined),
    },
  },
  integrations: [
    starlight({
      title: 'Orvano',
      description:
        'Orvano is the open source backend you host yourself: auth, Postgres, storage, functions, realtime, messaging, webhooks, jobs, and backups, run from one console.',
      logo: { src: './src/assets/logo.svg' },
      favicon: '/favicon.svg',
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/orvanohq/orvano' }],
      editLink: { baseUrl: 'https://github.com/orvanohq/orvano/edit/main/website/' },
      lastUpdated: false,
      head,
      customCss: [
        '@fontsource-variable/inter',
        '@fontsource-variable/jetbrains-mono',
        './src/styles/brand.css',
      ],
      components: {
        SiteTitle: './src/components/SiteTitle.astro',
      },
      // Code block themes and plugins: ec.config.mjs.
      // AC-6: these groups, in this order.
      sidebar: [
        {
          label: 'Get started',
          items: [
            { label: 'Overview', slug: 'docs' },
            { label: 'Run Orvano locally', slug: 'docs/local' },
            { label: 'Next.js quickstart', slug: 'docs/quickstarts/nextjs' },
            { label: 'Flutter quickstart', slug: 'docs/quickstarts/flutter' },
            { label: 'JavaScript quickstart', slug: 'docs/quickstarts/js' },
            { label: 'Dart server quickstart', slug: 'docs/quickstarts/dart' },
            { label: '.NET quickstart', slug: 'docs/quickstarts/dotnet' },
          ],
        },
        { label: 'Concepts', items: [{ autogenerate: { directory: 'docs/concepts' } }] },
        { label: 'Auth guides', items: [{ autogenerate: { directory: 'docs/auth' } }] },
        { label: 'SDKs', items: [{ autogenerate: { directory: 'docs/sdks' } }] },
        { label: 'Console', items: [{ autogenerate: { directory: 'docs/console' } }] },
        { label: 'Self hosting', items: [{ autogenerate: { directory: 'docs/self-hosting' } }] },
        // AC-15: one page per public operation, from the copy scripts/prepare.ts writes with the SDK snippets.
        { label: 'API reference', items: openAPISidebarGroups },
        { label: 'Errors', items: [{ label: 'Every error code', slug: 'errors' }] },
        { label: 'Changelog', slug: 'docs/changelog' },
      ],
      plugins: [
        starlightOpenAPI([
          {
            base: 'docs/api',
            schema: './.generated/openapi.docs.json',
            sidebar: {
              label: 'Operations',
              collapsed: false,
              operations: { labels: 'operationId' },
            },
          },
        ]),
        // AC-5: /llms.txt and /llms-full.txt for coding agents. scripts/markdown-copies.ts adds a `.md` copy of every page.
        starlightLlmsTxt({
          projectName: 'Orvano',
          details: [
            'Every page also has a plain Markdown copy: drop the trailing slash and add `.md`, for example https://orvano.dev/docs/local.md or https://orvano.dev/docs/api/operations/usersget.md.',
            '',
            '- Client SDKs (`@orvano/js`, `@orvano/nextjs`, `orvano_flutter`) sign users in and act as them. Server SDKs (`@orvano/js/server`, `orvano_dart`, the `Orvano` NuGet package) use an API key and verify access tokens.',
            '- Every failed call answers with RFC 9457 problem details; its `code` has a page at https://orvano.dev/errors/<code>.',
          ].join('\n'),
          optionalLinks: [
            {
              label: 'Error codes',
              url: 'https://orvano.dev/errors/',
              description: 'every error code and how to fix it',
            },
            {
              label: 'Source',
              url: 'https://github.com/orvanohq/orvano',
              description: 'the Orvano repository, examples included',
            },
          ],
          promote: ['docs', 'docs/local', 'docs/quickstarts/**', 'docs/concepts/**'],
          demote: ['errors/**'],
        }),
        // The reference pages are routes of starlight-openapi, which this validator can't see; scripts/prepare.ts
        // checks every link to them against the contract instead.
        starlightLinksValidator({ exclude: ['/docs/api/', '/docs/api/**'] }),
      ],
    }),
  ],
})
