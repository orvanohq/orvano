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

/** The settings a server quickstart reads from the environment, per shell (AC-12, AC-13). */
export const serverSettings = {
  unix: [
    `export ORVANO_ENDPOINT=${localUrl}`,
    'export ORVANO_PROJECT=your-project-id',
    'export ORVANO_API_KEY=your-api-key',
  ].join('\n'),
  powershell: [
    `$env:ORVANO_ENDPOINT = "${localUrl}"`,
    '$env:ORVANO_PROJECT = "your-project-id"',
    '$env:ORVANO_API_KEY = "your-api-key"',
  ].join('\n'),
}

/**
 * Signs in as the quickstart user with one call to `POST /v1/account/sessions/password`, then calls the example
 * server's `GET /me` with the access token, per shell (AC-12, AC-13).
 */
export function tokenCommands(port: number): { unix: string; powershell: string } {
  const body = '{"email":"ada@example.com","password":"a long password"}'
  return {
    unix: [
      `curl -s ${localUrl}/v1/account/sessions/password \\`,
      '  -H "X-Orvano-Project: $ORVANO_PROJECT" -H "Content-Type: application/json" \\',
      `  -d '${body}'`,
      '',
      '# Copy session.accessToken from the answer, then:',
      'TOKEN=paste-the-access-token-here',
      `curl -i http://localhost:${String(port)}/me -H "Authorization: Bearer $TOKEN"`,
    ].join('\n'),
    powershell: [
      `$session = (Invoke-RestMethod -Method Post -Uri ${localUrl}/v1/account/sessions/password \``,
      '  -Headers @{ "X-Orvano-Project" = $env:ORVANO_PROJECT } -ContentType "application/json" `',
      `  -Body '${body}').session`,
      `Invoke-RestMethod -Uri http://localhost:${String(port)}/me \``,
      '  -Headers @{ Authorization = "Bearer $($session.accessToken)" }',
    ].join('\n'),
  }
}
