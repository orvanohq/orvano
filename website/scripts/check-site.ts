/**
 * The CI check for the built site (spec 0011, AC-5, AC-7, AC-23, AC-24). It serves `dist/` the way Cloudflare does
 * (the headers from `dist/_headers`, trailing slash redirects, the 404 page for an unknown path), then in Chromium:
 *
 * - runs axe (WCAG 2.2 AA) on the landing page, a quickstart, a guide, an API reference page, an error page, and the
 *   404 page, in the dark and the light theme;
 * - fails on any Content Security Policy violation, search included, and on any request to another origin than the
 *   site's own and the analytics beacon's;
 * - checks that an unknown `/errors/<code>` answers 404 with a link to `/errors/`.
 *
 * It also checks every built page for `noindex` and the beacon, against `ORVANO_SITE_ENV`.
 *
 *   ORVANO_SITE_ENV=preview pnpm --filter @orvano/website check:site
 */
import { existsSync } from 'node:fs'
import { readdir, readFile, stat } from 'node:fs/promises'
import { createServer, type ServerResponse } from 'node:http'
import type { AddressInfo } from 'node:net'
import { createRequire } from 'node:module'
import { extname, join, normalize, relative, sep } from 'node:path'
import type { AxeResults } from 'axe-core'
import { chromium, type Page } from 'playwright'

const dist = join(import.meta.dirname, '..', 'dist')
const siteEnv = process.env.ORVANO_SITE_ENV
if (siteEnv !== 'preview' && siteEnv !== 'production') {
  throw new Error('Set ORVANO_SITE_ENV to the value dist/ was built with: preview or production.')
}

/** One page of each type (AC-7), plus the 404 page an unknown error code lands on (AC-23). */
const pages = [
  '/',
  '/docs/quickstarts/nextjs/',
  '/docs/auth/email-and-password/',
  '/docs/api/operations/accountcreate/',
  '/errors/invalid_credentials/',
  '/errors/not_a_code/',
]
const themes = ['dark', 'light'] as const
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']
const beaconOrigins = ['https://static.cloudflareinsights.com', 'https://cloudflareinsights.com']

const failures: string[] = []

const contentTypes: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.json': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.webp': 'image/webp',
  '.avif': 'image/avif',
  '.woff2': 'font/woff2',
  '.wasm': 'application/wasm',
  '.txt': 'text/plain; charset=utf-8',
  '.md': 'text/markdown; charset=utf-8',
  '.xml': 'application/xml',
}

// Every page carries `noindex` on a preview and never in production (AC-24); only production loads the beacon.
for (const file of await htmlFiles(dist)) {
  const html = await readFile(file, 'utf8')
  const page = relative(dist, file)
  const noindex = html.includes('<meta name="robots" content="noindex"')
  const beacon = html.includes('static.cloudflareinsights.com/beacon.min.js')
  if (siteEnv === 'preview' && !noindex) failures.push(`${page}: a preview page without noindex`)
  if (siteEnv === 'preview' && beacon)
    failures.push(`${page}: a preview page with the analytics beacon`)
  if (siteEnv === 'production' && noindex) failures.push(`${page}: a production page with noindex`)
}

const headerRules = parseHeaders(await readFile(join(dist, '_headers'), 'utf8'))
const policy = headersFor('/').get('content-security-policy') ?? ''
if (!policy.includes("script-src 'self' 'wasm-unsafe-eval' 'sha256-")) {
  failures.push(
    'dist/_headers has no Content Security Policy with script hashes. Did scripts/headers.ts run?',
  )
}

const server = createServer((request, response) => {
  serve(new URL(request.url ?? '/', 'http://localhost').pathname, response).catch(
    (error: unknown) => {
      response.statusCode = 500
      response.end(String(error))
    },
  )
})
await new Promise<void>((done) => server.listen(0, '127.0.0.1', done))
const origin = `http://localhost:${String((server.address() as AddressInfo).port)}`
const axeSource = await readFile(
  createRequire(import.meta.url).resolve('axe-core/axe.min.js'),
  'utf8',
)

const browser = await chromium.launch()
try {
  for (const theme of themes) {
    const context = await browser.newContext({ colorScheme: theme })
    // Starlight reads the reader's choice from here before it paints.
    await context.addInitScript((value) => {
      localStorage.setItem('starlight-theme', value)
      document.addEventListener('securitypolicyviolation', (event) => {
        const seen = (window as unknown as { cspViolations?: string[] }).cspViolations ?? []
        seen.push(`${event.effectiveDirective} blocked ${event.blockedURI || 'an inline script'}`)
        ;(window as unknown as { cspViolations: string[] }).cspViolations = seen
      })
    }, theme)
    // The beacon would count a page view from CI; answer it locally instead.
    await context.route('https://static.cloudflareinsights.com/**', (route) =>
      route.fulfill({ status: 200, contentType: 'text/javascript', body: '' }),
    )
    const page = await context.newPage()
    page.setDefaultTimeout(20_000)
    page.on('request', (request) => {
      const url = new URL(request.url())
      if (url.protocol === 'data:' || url.protocol === 'blob:') return
      if (url.origin !== origin && !beaconOrigins.includes(url.origin))
        failures.push(`${page.url()} (${theme}): requested ${request.url()}`)
    })

    for (const path of pages) {
      const response = await page.goto(`${origin}${path}`, { waitUntil: 'load' })
      const label = `${path} (${theme})`
      if (path === '/errors/not_a_code/') {
        if (response?.status() !== 404)
          failures.push(`${label}: answered ${String(response?.status())}, not 404`)
        if ((await page.locator('main a[href="/errors/"]').count()) === 0)
          failures.push(`${label}: the 404 page has no link to /errors/`)
      } else if (response?.status() !== 200) {
        failures.push(`${label}: answered ${String(response?.status())}`)
      }
      if ((await page.locator('html').getAttribute('data-theme')) !== theme)
        failures.push(`${label}: did not render in the ${theme} theme`)
      if (path === '/') await search(page, label)
      await page.evaluate(axeSource)
      const violations = await page.evaluate(async (tags) => {
        const { axe } = window as unknown as {
          axe: { run(context: Document, options: object): Promise<AxeResults> }
        }
        const results = await axe.run(document, { runOnly: { type: 'tag', values: tags } })
        return results.violations.map(
          (violation) =>
            `${violation.id}: ${violation.help} (${violation.nodes.map((node) => node.target.join(' ')).join(', ')})`,
        )
      }, axeTags)
      for (const violation of violations) failures.push(`${label}: ${violation}`)
      for (const violation of await page.evaluate(
        () => (window as unknown as { cspViolations?: string[] }).cspViolations ?? [],
      ))
        failures.push(`${label}: Content Security Policy: ${violation}`)
    }
    await context.close()
  }
} finally {
  await browser.close()
  server.close()
}

if (failures.length > 0) {
  console.error(
    `The site check found ${String(failures.length)} problems:\n  ${failures.join('\n  ')}`,
  )
  process.exit(1)
}
console.log(
  `${String(pages.length)} pages pass axe and the Content Security Policy in both themes; every page matches ${siteEnv}.`,
)

/** Searches the docs, so Pagefind's script and WebAssembly load under the policy. */
async function search(page: Page, label: string): Promise<void> {
  await page.locator('button[data-open-modal]').click()
  await page.locator('#starlight__search input').fill('quickstart')
  try {
    await page.locator('#starlight__search .pagefind-ui__result').first().waitFor()
  } catch {
    failures.push(`${label}: searching for "quickstart" showed no results`)
  }
  await page.keyboard.press('Escape')
}

/** Serves one path from `dist/` like Workers static assets with `auto-trailing-slash` and `404-page`. */
async function serve(path: string, response: ServerResponse): Promise<void> {
  const file = normalize(join(dist, decodeURIComponent(path)))
  if (file !== dist && !file.startsWith(dist + sep)) return send(response, 404, '/404.html', path)
  if (path.endsWith('/')) {
    if (existsSync(join(file, 'index.html')))
      return send(response, 200, join(path, 'index.html'), path)
  } else if (existsSync(join(file, 'index.html'))) {
    response.writeHead(307, { location: `${path}/` }).end()
    return
  } else if (existsSync(file) && (await stat(file)).isFile()) {
    return send(response, 200, path, path)
  } else if (existsSync(`${file}.html`)) {
    return send(response, 200, `${path}.html`, path)
  }
  return send(response, 404, '/404.html', path)
}

async function send(
  response: ServerResponse,
  status: number,
  file: string,
  path: string,
): Promise<void> {
  const headers = headersFor(path)
  headers.set('content-type', contentTypes[extname(file)] ?? 'application/octet-stream')
  response.writeHead(status, Object.fromEntries(headers))
  response.end(await readFile(join(dist, file)))
}

/** The `_headers` rules: a path pattern (`*` matches anything) and the headers it sets. */
function parseHeaders(text: string): { pattern: RegExp; headers: [string, string][] }[] {
  const rules: { pattern: RegExp; headers: [string, string][] }[] = []
  for (const line of text.split('\n')) {
    if (line.trim() === '' || line.trim().startsWith('#')) continue
    if (!/^\s/.test(line)) {
      const source = line
        .trim()
        .replace(/[.+?^${}()|[\]\\]/g, '\\$&')
        .replace(/\*/g, '.*')
      rules.push({ pattern: new RegExp(`^${source}$`), headers: [] })
      continue
    }
    const colon = line.indexOf(':')
    rules
      .at(-1)
      ?.headers.push([line.slice(0, colon).trim().toLowerCase(), line.slice(colon + 1).trim()])
  }
  return rules
}

function headersFor(path: string): Map<string, string> {
  const headers = new Map<string, string>()
  for (const rule of headerRules) {
    if (rule.pattern.test(path)) for (const [name, value] of rule.headers) headers.set(name, value)
  }
  return headers
}

async function htmlFiles(dir: string): Promise<string[]> {
  const files: string[] = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) files.push(...(await htmlFiles(path)))
    else if (entry.name.endsWith('.html')) files.push(path)
  }
  return files
}
