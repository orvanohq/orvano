// Refreshes the two lists Orvano.Auth embeds (spec 0014, AC-7): the disposable email domains (run it before each
// release) and, with --passwords, the common password list. It downloads each source at its current commit, applies
// the same normalization the server uses, and writes the files the server embeds. Then update the commit SHAs in
// THIRD_PARTY_NOTICES.md with the ones it prints.
//
//   node tools/lists/refresh.mjs              # the disposable domains only
//   node tools/lists/refresh.mjs --passwords  # both lists
import { writeFile } from 'node:fs/promises'
import { domainToASCII } from 'node:url'

const listsDir = new URL('../../server/src/Orvano.Auth/Lists/', import.meta.url)

const disposable = {
  repo: 'disposable-email-domains/disposable-email-domains',
  branch: 'main',
  path: 'disposable_email_blocklist.conf',
  out: 'disposable-domains.txt',
}

const passwords = {
  repo: 'danielmiessler/SecLists',
  branch: 'master',
  path: 'Passwords/Common-Credentials/xato-net-10-million-passwords-100000.txt',
  out: 'common-passwords.txt',
}

/** The latest commit of a branch, so the notice can name exactly what was taken. */
async function headCommit(repo, branch) {
  const response = await fetch(`https://api.github.com/repos/${repo}/commits/${branch}`, {
    headers: { Accept: 'application/vnd.github+json' },
  })
  if (!response.ok) throw new Error(`${repo}: GitHub answered ${String(response.status)}`)
  const body = await response.json()
  return body.sha
}

async function download(source) {
  const sha = await headCommit(source.repo, source.branch)
  const response = await fetch(
    `https://raw.githubusercontent.com/${source.repo}/${sha}/${source.path}`,
  )
  if (!response.ok) throw new Error(`${source.path}: ${String(response.status)}`)
  return { sha, text: await response.text() }
}

/**
 * AC-8's form: lowercase, ASCII (IDNA), no trailing dot, at least one dot. The server's own list rule refuses anything
 * else, so a malformed upstream line is dropped here rather than shipped.
 */
function normalizeDomain(line) {
  const trimmed = line.trim().toLowerCase().replace(/\.$/, '')
  if (trimmed === '' || trimmed.startsWith('#')) return null
  const ascii = domainToASCII(trimmed)
  return ascii !== '' && ascii.includes('.') && /^[a-z0-9.-]+$/.test(ascii) ? ascii : null
}

/**
 * AC-5's form: NFKC, then lowercase. Entries shorter than 8 code points are dropped, since the 8 character floor
 * refuses those passwords before the list is read; longer than 256 can never be a password either.
 */
function normalizePassword(line) {
  const form = line.replace(/\r$/, '').normalize('NFKC').toLowerCase()
  const length = [...form].length
  return length >= 8 && length <= 256 ? form : null
}

async function refresh(source, normalize, limit) {
  const { sha, text } = await download(source)
  const seen = new Set()
  for (const line of text.split('\n')) {
    const entry = normalize(line)
    if (entry !== null) seen.add(entry)
    if (seen.size >= limit) break
  }
  const entries = [...seen].sort()
  await writeFile(new URL(source.out, listsDir), `${entries.join('\n')}\n`)
  console.log(`${source.out}: ${String(entries.length)} entries from ${source.repo}@${sha}`)
}

await refresh(disposable, normalizeDomain, Number.POSITIVE_INFINITY)
if (process.argv.includes('--passwords')) await refresh(passwords, normalizePassword, 100_000)
