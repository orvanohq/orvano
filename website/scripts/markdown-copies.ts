/**
 * The postbuild step of orvano.dev (spec 0011, AC-5): writes a Markdown copy of every built page beside it, so a
 * coding agent can read `/docs/local.md` instead of scraping `/docs/local/`. It reads each page's rendered content
 * from `dist/`, so the generated error and API reference pages get a copy too, and quickstart code pulled from
 * `examples/` comes out as plain code blocks.
 *
 *   node scripts/markdown-copies.ts
 */
import type { Element, ElementContent, Root, RootContent } from 'hast'
import { select, selectAll } from 'hast-util-select'
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { join, relative } from 'node:path'
import rehypeParse from 'rehype-parse'
import rehypeRemark from 'rehype-remark'
import remarkGfm from 'remark-gfm'
import remarkStringify from 'remark-stringify'
import { unified } from 'unified'

const dist = join(import.meta.dirname, '..', 'dist')

const toMarkdown = unified()
  .use(rehypeRemark)
  .use(remarkGfm)
  .use(remarkStringify, { bullet: '-', fences: true, rule: '-' })

function text(node: Root | RootContent | ElementContent): string {
  if (node.type === 'text') return node.value
  if ('children' in node) return node.children.map(text).join('')
  return ''
}

function element(
  tagName: string,
  properties: Element['properties'],
  children: ElementContent[],
): Element {
  return { type: 'element', tagName, properties, children }
}

/** Swaps every node matching `selector` under `root` for what `replace` returns (nothing removes it). */
function replaceAll(
  root: Element,
  selector: string,
  replace: (node: Element) => ElementContent[],
): void {
  for (const node of selectAll(selector, root)) {
    const parent = findParent(root, node)
    if (parent === undefined) continue
    parent.children.splice(parent.children.indexOf(node), 1, ...replace(node))
  }
}

function findParent(root: Element, target: Element): Element | undefined {
  for (const child of root.children) {
    if (child === target) return root
    if (child.type === 'element') {
      const found = findParent(child, target)
      if (found !== undefined) return found
    }
  }
  return undefined
}

/** The Markdown for one built page, or undefined when it has no docs content (the landing page's hero, the 404). */
function pageToMarkdown(html: string, ownDescription: boolean): string | undefined {
  const tree = unified().use(rehypeParse).parse(html)
  const content = select('.sl-markdown-content', tree)
  if (content === undefined) return undefined
  const title = text(select('h1', tree) ?? element('h1', {}, [])).trim()
  const description = select('meta[name="description"]', tree)?.properties.content

  // The API reference shows one SDK snippet at a time behind a picker; label each snippet with its SDK instead.
  const snippetLabels = new Map(
    selectAll('sl-openapi-snippet-picker option', content).map((option) => [
      String(option.properties.value),
      text(option).trim(),
    ]),
  )
  replaceAll(content, 'sl-openapi-snippet-picker', () => [])
  replaceAll(content, '.sl-openapi-snippet', (snippet) => [
    element('p', {}, [
      element('strong', {}, [
        {
          type: 'text',
          value: snippetLabels.get(String(snippet.properties.dataOpenapiSnippetId)) ?? '',
        },
      ]),
    ]),
    ...snippet.children,
  ])
  // Interface parts that mean nothing in Markdown.
  replaceAll(content, 'script, style, link, .sr-only, .copy, .sl-anchor-link, .shot-dark', () => [])
  // Expressive Code renders a line per element; turn each block back into a plain fenced block.
  replaceAll(content, '.expressive-code', (block) => {
    const pre = select('pre', block)
    const caption = text(select('.title', block) ?? element('span', {}, [])).trim()
    const lines = selectAll('.ec-line', block).map((line) => text(line).replace(/\n$/, ''))
    const lang = String(pre?.properties.dataLanguage ?? '')
    const code = element('pre', {}, [
      element('code', lang === '' ? {} : { className: [`language-${lang}`] }, [
        { type: 'text', value: lines.join('\n') },
      ]),
    ])
    return caption === ''
      ? [code]
      : [element('p', {}, [element('code', {}, [{ type: 'text', value: caption }])]), code]
  })
  // Tabs become a bold label above each panel.
  replaceAll(content, 'starlight-tabs', (tabs) => {
    const labels = selectAll('[role="tab"]', tabs).map((tab) => text(tab).trim())
    return selectAll('[role="tabpanel"]', tabs).flatMap((panel, i) => [
      element('p', {}, [element('strong', {}, [{ type: 'text', value: labels[i] ?? '' }])]),
      ...panel.children,
    ])
  })

  const body = toMarkdown.stringify(toMarkdown.runSync(rootOf(content)))
  const intro =
    ownDescription && typeof description === 'string' && description !== ''
      ? `\n\n> ${description}`
      : ''
  return `# ${title}${intro}\n\n${body.trim()}\n`
}

function rootOf(node: Element): Root {
  return { type: 'root', children: node.children }
}

async function listPages(dir: string): Promise<string[]> {
  const pages: string[] = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory() && !['_astro', 'pagefind'].includes(entry.name))
      pages.push(...(await listPages(path)))
    else if (entry.name === 'index.html' && dir !== dist) pages.push(path)
  }
  return pages
}

let written = 0
for (const page of await listPages(dist)) {
  // API reference pages have no description of their own; they carry the site's, so leave it out.
  const ownDescription = !relative(dist, page).startsWith('docs/api/')
  const markdown = pageToMarkdown(await readFile(page, 'utf8'), ownDescription)
  if (markdown === undefined) continue
  // dist/docs/local/index.html → dist/docs/local.md, served at /docs/local.md.
  await writeFile(`${join(page, '..')}.md`, markdown)
  written++
}
console.log(
  `Wrote a Markdown copy of ${String(written)} pages in ${relative(process.cwd(), dist)}/.`,
)
