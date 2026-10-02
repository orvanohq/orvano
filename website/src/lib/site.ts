import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

/** The repo root: the nearest folder above the build's working directory that holds `VERSION`. */
function findRepoRoot(): string {
  let dir = process.cwd()
  while (!existsSync(join(dir, 'VERSION'))) {
    const parent = dirname(dir)
    if (parent === dir) throw new Error('No VERSION file above the website folder.')
    dir = parent
  }
  return dir
}

/** The repo root, where `examples/`, `contract/`, and `CHANGELOG.md` live. */
export const repoRoot = findRepoRoot()

/** The Orvano version these docs document, from `VERSION` at build time (AC-8). */
export const version = readFileSync(join(repoRoot, 'VERSION'), 'utf8').trim()

/** The server image every command uses, at exactly this version, never `latest` (AC-8). */
export const serverImage = `ghcr.io/orvanohq/orvano:${version}`

/** The local stack's console and API (spec 0011, AC-1). Always `localhost`, never `127.0.0.1`. */
export const localUrl = 'http://localhost:7700'

/** The local stack's Mailpit inbox. */
export const inboxUrl = 'http://localhost:8025'

/** The three commands that start a local stack, per shell (AC-13). */
export const localStart = {
  unix: [
    'mkdir orvano-local && cd orvano-local',
    `docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/install" ${serverImage} install --local --yes`,
    'docker compose up -d --wait',
  ].join('\n'),
  powershell: [
    'mkdir orvano-local; cd orvano-local',
    `docker run --rm -v "\${PWD}:/install" ${serverImage} install --local --yes`,
    'docker compose up -d --wait',
  ].join('\n'),
}
