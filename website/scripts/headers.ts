/**
 * The postbuild step that finishes `dist/_headers` (spec 0011, AC-23): it hashes every inline script in the built
 * pages and adds the Content Security Policy to the `/*` rule `public/_headers` starts. Starlight's inline scripts
 * (the theme provider, the search dialog, the sidebar) are the same on every page, so the list stays short. The build
 * fails when a page uses an inline event handler (a hash can't allow it) or the policy outgrows Cloudflare's 2,000
 * character header line.
 *
 *   node scripts/headers.ts
 */
import type { Element, ElementContent, Root } from 'hast'
import { createHash } from 'node:crypto'
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { join, relative } from 'node:path'
import rehypeParse from 'rehype-parse'
import { unified } from 'unified'

const dist = join(import.meta.dirname, '..', 'dist')

/** Cloudflare's limit for one line of `_headers`. */
const maxLineLength = 2000

/** Script types the browser runs, so their hashes go in `script-src`. JSON and other data blocks don't. */
const runnable = new Set(['', 'module', 'text/javascript', 'application/javascript'])

async function htmlFiles(dir: string): Promise<string[]> {
  const files: string[] = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) files.push(...(await htmlFiles(path)))
    else if (entry.name.endsWith('.html')) files.push(path)
  }
  return files
}

const parser = unified().use(rehypeParse)

/** Every element under `node`, depth first. */
function* elements(node: Root | Element): Generator<Element> {
  for (const child of node.children) {
    if (child.type !== 'element') continue
    yield child
    yield* elements(child)
  }
}

function text(nodes: ElementContent[]): string {
  return nodes.map((node) => (node.type === 'text' ? node.value : '')).join('')
}

const hashes = new Set<string>()
const handlers = new Set<string>()
for (const file of await htmlFiles(dist)) {
  // Parsed like the browser parses it, so a hash covers exactly the text the browser checks.
  const tree = parser.parse(await readFile(file, 'utf8'))
  for (const element of elements(tree)) {
    if (Object.keys(element.properties).some((name) => /^on[A-Z]/.test(name)))
      handlers.add(relative(dist, file))
    if (element.tagName !== 'script' || element.properties.src !== undefined) continue
    const type = element.properties.type
    if (!runnable.has(typeof type === 'string' ? type.toLowerCase() : '')) continue
    hashes.add(`'sha256-${createHash('sha256').update(text(element.children)).digest('base64')}'`)
  }
}

if (handlers.size > 0) {
  throw new Error(
    `These pages use an inline event handler, which the Content Security Policy blocks:\n  ${[...handlers].join('\n  ')}`,
  )
}

const policy = [
  "default-src 'self'",
  // Pagefind compiles its WebAssembly; the beacon is Cloudflare Web Analytics (production builds only, AC-24).
  `script-src 'self' 'wasm-unsafe-eval' ${[...hashes].sort().join(' ')} https://static.cloudflareinsights.com`,
  // Starlight and its code blocks set inline styles.
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data:",
  "font-src 'self'",
  "connect-src 'self' https://cloudflareinsights.com",
  "frame-ancestors 'none'",
  "base-uri 'self'",
  "form-action 'self'",
].join('; ')

const line = `  Content-Security-Policy: ${policy}`
if (line.length > maxLineLength) {
  throw new Error(
    `The Content Security Policy is ${String(line.length)} characters, over Cloudflare's ${String(maxLineLength)} for one _headers line. Look for an inline script that differs per page.`,
  )
}

const headersFile = join(dist, '_headers')
const lines = (await readFile(headersFile, 'utf8')).split('\n')
if (lines.some((text) => /^\s+Content-Security-Policy:/i.test(text))) {
  throw new Error(
    'dist/_headers already has a Content Security Policy. Build again from a clean dist/.',
  )
}
const all = lines.findIndex((text) => text.trim() === '/*')
if (all === -1) throw new Error('public/_headers has no `/*` rule for the policy to join.')
let end = all + 1
while (end < lines.length && /^\s+\S/.test(lines[end] ?? '')) end++
lines.splice(end, 0, line)
await writeFile(headersFile, lines.join('\n'))

console.log(
  `Added the Content Security Policy with ${String(hashes.size)} inline script hashes to dist/_headers.`,
)
