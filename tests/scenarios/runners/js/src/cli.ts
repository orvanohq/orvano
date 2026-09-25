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
import { createSurface } from './surface.js'

const here = dirname(fileURLToPath(import.meta.url))
const packageDir = resolve(here, '..')
const scenariosDir = resolve(packageDir, '../..')
const endpoint = (process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080').replace(/\/+$/, '')
const marker = 'ORVANO_SCENARIO_RESULTS '

type Target = 'node' | 'bun' | 'deno' | 'browser' | 'workerd' | 'nextjs'
const targets: readonly Target[] = ['node', 'bun', 'deno', 'browser', 'workerd', 'nextjs']

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
  })
  const line = stdout.split('\n').find((l) => l.startsWith(marker))
  if (line === undefined) throw new Error(`no results from ${command}:\n${stdout}`)
  return JSON.parse(line.slice(marker.length)) as ScenarioResult[]
}

/** Chromium through Playwright, on a page that proxies /v1 to Orvano so calls are same origin. */
async function inBrowser(scenarios: Scenario[]): Promise<ScenarioResult[]> {
  const { chromium } = await import('playwright')
  const bundle = await readFile(join(packageDir, 'dist/bundles/browser.js'))
  const server = createServer((req, res) => {
    void (async () => {
      const url = req.url ?? '/'
      if (url.startsWith('/v1/')) {
        const upstream = await fetch(endpoint + url, {
          method: req.method ?? 'GET',
          headers: { accept: 'application/json' },
        })
        res.writeHead(upstream.status, Object.fromEntries(upstream.headers))
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
    const page = await browser.newPage()
    await page.goto(origin)
    await page.waitForFunction(() => typeof window.orvanoRunScenarios === 'function')
    return await page.evaluate((s) => window.orvanoRunScenarios(s), scenarios)
  } finally {
    await browser.close()
    server.close()
  }
}

/** workerd through Miniflare: the bundled worker runs the scenarios and returns the results. */
async function inWorkerd(scenarios: Scenario[]): Promise<ScenarioResult[]> {
  const { Miniflare } = await import('miniflare')
  const mf = new Miniflare({
    modules: true,
    scriptPath: join(packageDir, 'dist/bundles/worker.js'),
    compatibilityDate: '2026-07-30',
    bindings: { ORVANO_ENDPOINT: endpoint },
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
    env: { ...process.env, ORVANO_ENDPOINT: endpoint },
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
      return runScenarios(scenarios, createSurface(endpoint))
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
