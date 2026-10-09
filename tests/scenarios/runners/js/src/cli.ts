// Runs the shared scenarios on one JS surface against the Orvano at ORVANO_ENDPOINT
// (default http://localhost:8080). Usage: node dist/cli.js <node|bun|deno|browser|workerd|nextjs>
import { spawn } from 'node:child_process'
import { once } from 'node:events'
import { mkdtemp, readFile, readdir, writeFile } from 'node:fs/promises'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { parse } from 'yaml'
import { runScenarios } from './interpreter.js'
import type { Scenario, ScenarioResult } from './interpreter.js'
import { createSurface, projectSurfaces } from './surface.js'
import type { ConsoleUser } from './surface.js'
import type { BrowserContext } from 'playwright'

const here = dirname(fileURLToPath(import.meta.url))
const packageDir = resolve(here, '..')
const scenariosDir = resolve(packageDir, '../..')
const endpoint = (process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080').replace(/\/+$/, '')
const marker = 'ORVANO_SCENARIO_RESULTS '

type Target = 'node' | 'bun' | 'deno' | 'browser' | 'workerd' | 'nextjs'
const targets: readonly Target[] = ['node', 'bun', 'deno', 'browser', 'workerd', 'nextjs']

/**
 * The first console account, the first project, and its API key in `fixtures.yaml`, which the
 * server seeds in the Test environment.
 */
async function loadFixtures(): Promise<{
  consoleUser?: ConsoleUser
  project?: string
  apiKey?: string
}> {
  const fixtures = parse(await readFile(join(scenariosDir, 'fixtures.yaml'), 'utf8')) as {
    consoleUsers?: { email?: string; password?: string }[]
    projects?: { id?: string }[]
    apiKeys?: { project?: string; secret?: string }[]
  } | null
  const first = fixtures?.consoleUsers?.[0]
  const consoleUser =
    first?.email === undefined || first.password === undefined
      ? undefined
      : { email: first.email, password: first.password }
  const project = fixtures?.projects?.[0]?.id
  const apiKey = fixtures?.apiKeys?.find((k) => k.project === project)?.secret
  return {
    ...(consoleUser === undefined ? {} : { consoleUser }),
    ...(project === undefined ? {} : { project }),
    ...(apiKey === undefined ? {} : { apiKey }),
  }
}

const { consoleUser, project, apiKey } = await loadFixtures()
/** The fixtures every runtime needs, as environment variables. */
const fixtureEnv: Record<string, string> = {
  ...(consoleUser === undefined
    ? {}
    : { ORVANO_CONSOLE_EMAIL: consoleUser.email, ORVANO_CONSOLE_PASSWORD: consoleUser.password }),
  ...(project === undefined ? {} : { ORVANO_PROJECT: project }),
  ...(apiKey === undefined ? {} : { ORVANO_API_KEY: apiKey }),
}

async function loadScenarios(): Promise<Scenario[]> {
  const files = (await readdir(scenariosDir))
    .filter((f) => f.endsWith('.yaml') && f !== 'fixtures.yaml')
    .sort()
  return Promise.all(
    files.map(async (f) => parse(await readFile(join(scenariosDir, f), 'utf8')) as Scenario),
  )
}

/** Runs a command, returning its stdout; fails with its output when it exits non zero. */
async function exec(
  command: string,
  args: string[],
  env: Record<string, string> = {},
): Promise<string> {
  const child = spawn(command, args, {
    cwd: packageDir,
    env: { ...process.env, ...env },
    stdio: ['ignore', 'pipe', 'inherit'],
  })
  let stdout = ''
  child.stdout.setEncoding('utf8').on('data', (chunk: string) => (stdout += chunk))
  const [code] = (await once(child, 'close')) as [number | null]
  if (code !== 0)
    throw new Error(`${command} ${args.join(' ')} exited with ${String(code)}\n${stdout}`)
  return stdout
}

/** Runs the file runner in another runtime (Bun, Deno) and reads its results line. */
async function inRuntime(
  command: string,
  args: string[],
  scenarios: Scenario[],
): Promise<ScenarioResult[]> {
  const file = join(await mkdtemp(join(tmpdir(), 'orvano-scenarios-')), 'scenarios.json')
  await writeFile(file, JSON.stringify(scenarios))
  const stdout = await exec(command, [...args, 'dist/entries/file-runner.js'], {
    ORVANO_SCENARIOS_FILE: file,
    ORVANO_ENDPOINT: endpoint,
    ...fixtureEnv,
  })
  const line = stdout.split('\n').find((l) => l.startsWith(marker))
  if (line === undefined) throw new Error(`no results from ${command}:\n${stdout}`)
  return JSON.parse(line.slice(marker.length)) as ScenarioResult[]
}

/**
 * Chromium through Playwright, on a page that proxies /v1 to Orvano so calls are same origin. The
 * proxy passes headers (the browser's cookies included) and bodies through unchanged.
 */
async function inBrowser(scenarios: Scenario[]): Promise<ScenarioResult[]> {
  const { chromium } = await import('playwright')
  const bundle = await readFile(join(packageDir, 'dist/bundles/browser.js'))
  let refreshes = 0
  const server = createServer((req, res) => {
    void (async () => {
      const url = req.url ?? '/'
      if (url.startsWith('/v1/account/sessions/refresh')) refreshes++
      if (url.startsWith('/v1/')) {
        const headers = new Headers()
        for (const [name, value] of Object.entries(req.headers)) {
          if (name === 'host' || name === 'connection' || value === undefined) continue
          headers.set(name, Array.isArray(value) ? value.join(', ') : value)
        }
        const chunks: Buffer[] = []
        for await (const chunk of req) chunks.push(chunk as Buffer)
        const method = req.method ?? 'GET'
        const upstream = await fetch(endpoint + url, {
          method,
          headers,
          redirect: 'manual',
          ...(method === 'GET' || method === 'HEAD' ? {} : { body: Buffer.concat(chunks) }),
        })
        // Every Set-Cookie separately: a plain object would keep only the last one.
        const forwarded: [string, string][] = [...upstream.headers].filter(
          ([name]) => name !== 'set-cookie',
        )
        for (const cookie of upstream.headers.getSetCookie()) forwarded.push(['set-cookie', cookie])
        // A page can't read a redirect (spec 0012's provider flows): the runner's fetch asks for
        // it as a 200 with the target in a header instead.
        const location = upstream.headers.get('location')
        if (location !== null && req.headers['x-orvano-test-manual-redirect'] === '1') {
          res.writeHead(200, [
            ...forwarded.filter(([name]) => name !== 'location').flat(),
            'x-orvano-test-location',
            location,
          ])
          res.end()
          return
        }
        res.writeHead(upstream.status, forwarded.flat())
        res.end(Buffer.from(await upstream.arrayBuffer()))
      } else if (url === '/browser.js') {
        res.writeHead(200, { 'content-type': 'text/javascript' }).end(bundle)
      } else {
        res.writeHead(200, { 'content-type': 'text/html' })
        res.end(
          '<!doctype html><title>scenarios</title><script type="module" src="/browser.js"></script>',
        )
      }
    })().catch((error: unknown) => res.writeHead(502).end(String(error)))
  })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const origin = `http://127.0.0.1:${String((server.address() as AddressInfo).port)}`

  const browser = await chromium.launch()
  try {
    const context = await browser.newContext()
    const page = await context.newPage()
    await page.goto(origin)
    await page.waitForFunction(() => typeof window.orvanoRunScenarios === 'function')
    const results = await page.evaluate(([s, p, c]) => window.orvanoRunScenarios(s, p, c), [
      scenarios,
      project,
      consoleUser,
    ] as const)
    return [...results, await twoTabs(context, origin, () => refreshes)]
  } finally {
    await browser.close()
    server.close()
  }
}

/**
 * Spec 0004 AC-24 in a real browser: two tabs share one stored session about to expire and call
 * at once. Exactly one refresh reaches Orvano (the Web Lock), and the second tab hears
 * `tokenRefreshed`.
 */
async function twoTabs(
  context: BrowserContext,
  origin: string,
  refreshes: () => number,
): Promise<ScenarioResult> {
  const name = 'browser: two tabs refresh once, and the other tab hears it (spec 0004 AC-24)'
  try {
    const [first, second] = [await context.newPage(), await context.newPage()]
    for (const tab of [first, second]) {
      await tab.goto(origin)
      await tab.waitForFunction(() => typeof window.orvanoTabs === 'object')
    }
    await first.evaluate((p) => window.orvanoTabs.signUp(p), project)
    for (const tab of [first, second])
      await tab.evaluate((p) => {
        window.orvanoTabs.watch(p)
      }, project)
    const before = refreshes()
    const ids = await Promise.all(
      [first, second].map((tab) => tab.evaluate(() => window.orvanoTabs.call())),
    )
    await second.waitForTimeout(200)
    const events = await second.evaluate(() => window.orvanoTabs.events())
    const count = refreshes() - before
    if (ids[0] !== ids[1])
      return { name, outcome: 'failed', reason: 'the tabs saw different users' }
    if (count !== 1)
      return { name, outcome: 'failed', reason: `${String(count)} refreshes reached Orvano` }
    if (!events.includes('tokenRefreshed'))
      return { name, outcome: 'failed', reason: `the second tab heard ${JSON.stringify(events)}` }
    return { name, outcome: 'passed' }
  } catch (error) {
    return {
      name,
      outcome: 'failed',
      reason: error instanceof Error ? error.message : String(error),
    }
  }
}

/** workerd through Miniflare: the bundled worker runs the scenarios and returns the results. */
async function inWorkerd(scenarios: Scenario[]): Promise<ScenarioResult[]> {
  const { Miniflare } = await import('miniflare')
  const mf = new Miniflare({
    modules: true,
    scriptPath: join(packageDir, 'dist/bundles/worker.js'),
    compatibilityDate: '2026-07-30',
    bindings: {
      ORVANO_ENDPOINT: endpoint,
      ...fixtureEnv,
    },
  })
  try {
    const response = await mf.dispatchFetch('http://scenarios.invalid/', {
      method: 'POST',
      body: JSON.stringify(scenarios),
    })
    if (!response.ok)
      throw new Error(`worker answered ${String(response.status)}: ${await response.text()}`)
    return (await response.json()) as ScenarioResult[]
  } finally {
    await mf.dispose()
  }
}

/** A production Next.js build whose route handler runs the scenarios through @orvano/nextjs. */
async function inNextjs(scenarios: Scenario[]): Promise<ScenarioResult[]> {
  const appDir = resolve(packageDir, '../nextjs')
  const port = String(3100 + Math.floor(Math.random() * 800))
  const child = spawn('pnpm', ['exec', 'next', 'start', '--port', port], {
    cwd: appDir,
    env: {
      ...process.env,
      ORVANO_ENDPOINT: endpoint,
      ...fixtureEnv,
    },
    stdio: ['ignore', 'inherit', 'inherit'],
  })
  try {
    const origin = `http://127.0.0.1:${port}`
    for (let attempt = 0; ; attempt++) {
      try {
        // The server component page calls Orvano through createServerClient while rendering.
        const page = await fetch(origin + '/')
        if (!page.ok || !(await page.text()).includes('Orvano ')) {
          throw new Error(`the server component page answered ${String(page.status)}`)
        }
        const response = await fetch(origin + '/scenarios', {
          method: 'POST',
          body: JSON.stringify(scenarios),
        })
        if (!response.ok)
          throw new Error(`Next.js answered ${String(response.status)}: ${await response.text()}`)
        return (await response.json()) as ScenarioResult[]
      } catch (error) {
        if (attempt >= 60 || !(error instanceof TypeError)) throw error
        await new Promise((r) => setTimeout(r, 500))
      }
    }
  } finally {
    child.kill()
  }
}

async function run(target: Target, scenarios: Scenario[]): Promise<ScenarioResult[]> {
  switch (target) {
    case 'node':
      return runScenarios(
        scenarios,
        createSurface(endpoint, { consoleUser, project, apiKey }),
        projectSurfaces(endpoint),
      )
    case 'bun':
      return inRuntime('bun', ['run'], scenarios)
    case 'deno':
      return inRuntime('deno', ['run', '--allow-read', '--allow-env', '--allow-net'], scenarios)
    case 'browser':
      return inBrowser(scenarios)
    case 'workerd':
      return inWorkerd(scenarios)
    case 'nextjs':
      return inNextjs(scenarios)
  }
}

const target = process.argv[2] as Target | undefined
if (target === undefined || !targets.includes(target)) {
  console.error(`usage: scenarios <${targets.join('|')}>`)
  process.exit(2)
}

const results = await run(target, await loadScenarios())
for (const r of results)
  console.log(`${r.outcome.padEnd(7)} ${r.name}${r.reason === undefined ? '' : `: ${r.reason}`}`)
const failed = results.filter((r) => r.outcome === 'failed').length
const passed = results.filter((r) => r.outcome === 'passed').length
console.log(
  `${target}: ${String(passed)} passed, ${String(failed)} failed, ${String(results.length - passed - failed)} skipped`,
)
if (failed > 0 || passed === 0) process.exit(1)
