// orvano.dev (spec 0011): one static Starlight site. ORVANO_SITE_ENV decides `noindex` and the analytics beacon.
import starlight from '@astrojs/starlight'
import { defineConfig } from 'astro/config'
import starlightLinksValidator from 'starlight-links-validator'

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
      expressiveCode: {
        themes: ['github-dark-default', 'github-light-default'],
        styleOverrides: { codeFontFamily: 'var(--sl-font-mono)' },
      },
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
        { label: 'API reference', items: [{ autogenerate: { directory: 'docs/api' } }] },
        { label: 'Errors', items: [{ label: 'Every error code', slug: 'errors' }] },
        { label: 'Changelog', slug: 'docs/changelog' },
      ],
      plugins: [starlightLinksValidator()],
    }),
  ],
})
