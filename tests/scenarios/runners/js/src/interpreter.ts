import { OrvanoError, decodeEvent, eventRegistry } from '@orvano/js'
import type { DispatchEntry } from './dispatch-table.js'
import type { ClientSurface } from './generated/client.js'
import type { ConsoleSurface } from './generated/console.js'
import { consoleDispatch } from './generated/console-dispatch.js'
import { dispatch } from './generated/dispatch.js'
import type { ServerSurface } from './generated/server.js'
import { testEventRegistry } from './generated/test-events.js'

/** One step of a scenario file: an operation call (`op`) or an event decode (`event`). */
export interface ScenarioStep {
  op?: string
  as?: 'client' | 'server' | 'console'
  input?: Record<string, unknown>
  /** Walk every page with the SDK's async iterator; the body becomes `{ items: [...] }`. */
  paginate?: boolean
  event?: string
  /** For an event step: the raw payload to decode. */
  raw?: unknown
  expect: { status?: number; code?: string; body?: unknown }
  save?: Record<string, string>
}

/** A parsed scenario file from `tests/scenarios/`. */
export interface Scenario {
  name: string
  requires?: string[]
  steps: ScenarioStep[]
}

/** What happened to one scenario. */
export interface ScenarioResult {
  name: string
  outcome: 'passed' | 'failed' | 'skipped'
  /** Why it failed or was skipped. */
  reason?: string
}

/** The SDK objects a surface offers, one per role. Without `console`, console steps skip. */
export interface Surface {
  client: ClientSurface
  server: ServerSurface
  console?: ConsoleSurface | undefined
}

/** The package's events plus the test events, as the spec has runners decode them. */
const events = { ...eventRegistry, ...testEventRegistry }

class StepFailure extends Error {}

class ScenarioSkipped extends Error {}

/** Runs every scenario in order and reports each one. Never throws for a scenario failure. */
export async function runScenarios(
  scenarios: Scenario[],
  surface: Surface,
): Promise<ScenarioResult[]> {
  const results: ScenarioResult[] = []
  for (const scenario of scenarios) {
    try {
      await runScenario(scenario, surface)
      results.push({ name: scenario.name, outcome: 'passed' })
    } catch (error) {
      if (error instanceof ScenarioSkipped) {
        results.push({ name: scenario.name, outcome: 'skipped', reason: error.message })
      } else {
        const reason = error instanceof Error ? error.message : String(error)
        results.push({ name: scenario.name, outcome: 'failed', reason })
      }
    }
  }
  return results
}

async function runScenario(scenario: Scenario, surface: Surface): Promise<void> {
  const vars = new Map<string, unknown>()
  for (const [index, step] of scenario.steps.entries()) {
    const where = `step ${String(index + 1)} (${step.op ?? `event ${step.event ?? '?'}`}${step.as === undefined ? '' : ` as ${step.as}`})`
    const expect = substitute(step.expect, vars) as ScenarioStep['expect']

    let status: number | undefined
    let code: string | undefined
    let body: unknown
    if (step.event !== undefined) {
      const decoded = decodeEvent(step.event, substitute(step.raw, vars), events)
      if (decoded === undefined)
        throw new StepFailure(`${where}: the contract has no event ${step.event}`)
      body = JSON.parse(JSON.stringify(decoded)) as unknown
    } else {
      const table = step.as === 'console' ? consoleDispatch : dispatch
      const entry = step.op === undefined ? undefined : table[step.op]
      if (entry === undefined)
        throw new StepFailure(
          `${where}: the contract has no ${step.as ?? ''} operation ${step.op ?? '?'}`,
        )
      const input = substitute(step.input ?? {}, vars) as Record<string, unknown>
      try {
        body = await call(step, entry, surface, input)
        status = entry.status
      } catch (error) {
        if (!(error instanceof OrvanoError)) throw error
        status = error.status
        code = error.code
      }
    }

    if (expect.status !== undefined && status !== expect.status) {
      throw new StepFailure(
        `${where}: expected status ${String(expect.status)}, got ${String(status)}${code === undefined ? '' : ` (${code})`}`,
      )
    }
    if (expect.code !== undefined && code !== expect.code) {
      throw new StepFailure(`${where}: expected code ${expect.code}, got ${code ?? 'none'}`)
    }
    if (expect.body !== undefined) {
      const mismatch = subsetMismatch(expect.body, body, '$')
      if (mismatch !== null) throw new StepFailure(`${where}: body ${mismatch}`)
    }
    for (const [name, path] of Object.entries(step.save ?? {})) vars.set(name, select(body, path))
  }
}

async function call(
  step: ScenarioStep,
  entry: DispatchEntry,
  surface: Surface,
  input: Record<string, unknown>,
): Promise<unknown> {
  const op = step.op ?? '?'
  const all = step.paginate === true
  const missing = (sdk: string): ScenarioSkipped =>
    new ScenarioSkipped(`${op} has no ${all ? 'paged ' : ''}${step.as ?? ''} call in ${sdk}`)
  switch (step.as) {
    case 'client':
      if (all) {
        if (entry.clientAll === undefined) throw missing('@orvano/js')
        return collect(entry.clientAll(surface.client, input))
      }
      if (entry.client === undefined) throw missing('@orvano/js')
      return entry.client(surface.client, input)
    case 'server':
      if (all) {
        if (entry.serverAll === undefined) throw missing('@orvano/js/server')
        return collect(entry.serverAll(surface.server, input))
      }
      if (entry.server === undefined) throw missing('@orvano/js/server')
      return entry.server(surface.server, input)
    case 'console':
      if (surface.console === undefined)
        throw new ScenarioSkipped('no console session on this surface (fixtures.yaml)')
      if (all) {
        if (entry.consoleAll === undefined) throw missing('@orvano/console-client')
        return collect(entry.consoleAll(surface.console, input))
      }
      if (entry.console === undefined) throw missing('@orvano/console-client')
      return entry.console(surface.console, input)
    case undefined:
      throw new StepFailure(`${op}: an operation step needs \`as\``)
  }
}

async function collect(items: AsyncIterable<unknown>): Promise<{ items: unknown[] }> {
  const all: unknown[] = []
  for await (const item of items) all.push(item)
  return { items: all }
}

/** Replaces `${name}` with saved values. A string that is exactly `${name}` keeps the value's type. */
function substitute(value: unknown, vars: Map<string, unknown>): unknown {
  if (typeof value === 'string') {
    const whole = /^\$\{(\w+)\}$/.exec(value)
    if (whole?.[1] !== undefined) return lookup(vars, whole[1])
    return value.replace(/\$\{(\w+)\}/g, (_, name: string) => String(lookup(vars, name)))
  }
  if (Array.isArray(value)) return value.map((v) => substitute(v, vars))
  if (typeof value === 'object' && value !== null) {
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, substitute(v, vars)]))
  }
  return value
}

function lookup(vars: Map<string, unknown>, name: string): unknown {
  if (!vars.has(name)) throw new StepFailure(`\${${name}} was never saved by an earlier step`)
  return vars.get(name)
}

/** Reads a `$.a.b` path out of a response body. */
function select(body: unknown, path: string): unknown {
  if (!path.startsWith('$')) throw new StepFailure(`save path ${path} must start with $`)
  let current = body
  for (const key of path
    .slice(1)
    .split('.')
    .filter((k) => k !== '')) {
    if (typeof current !== 'object' || current === null)
      throw new StepFailure(`save path ${path} not found`)
    current = (current as Record<string, unknown>)[key]
  }
  return current
}

/** Null when every key in `expected` matches `actual`; otherwise where and how it differs. */
function subsetMismatch(expected: unknown, actual: unknown, at: string): string | null {
  if (Array.isArray(expected)) {
    if (!Array.isArray(actual) || actual.length !== expected.length)
      return `${at}: expected ${String(expected.length)} items`
    for (const [i, item] of expected.entries()) {
      const mismatch = subsetMismatch(item, actual[i], `${at}[${String(i)}]`)
      if (mismatch !== null) return mismatch
    }
    return null
  }
  if (typeof expected === 'object' && expected !== null) {
    if (typeof actual !== 'object' || actual === null) return `${at}: expected an object`
    for (const [key, value] of Object.entries(expected)) {
      const mismatch = subsetMismatch(
        value,
        (actual as Record<string, unknown>)[key],
        `${at}.${key}`,
      )
      if (mismatch !== null) return mismatch
    }
    return null
  }
  return expected === actual
    ? null
    : `${at}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`
}
