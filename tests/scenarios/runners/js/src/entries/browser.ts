// Bundled for Chromium. The page is served from the same origin that proxies /v1 to Orvano, so
// the SDK talks to location.origin exactly as an app on the Orvano domain would, and the browser
// sends the console cookie the runner set on that origin.
import * as app from '@orvano/js'
import { Client as ServerClient } from '@orvano/js/server'
import { runScenarios } from '../interpreter.js'
import type { Scenario, ScenarioResult } from '../interpreter.js'
import { createSurface, projectSurfaces } from '../surface.js'
import type { ConsoleUser } from '../surface.js'

declare global {
  interface Window {
    orvanoRunScenarios: (
      scenarios: Scenario[],
      project: string | undefined,
      consoleUser: ConsoleUser | undefined,
    ) => Promise<ScenarioResult[]>
    orvanoTabs: TabCheck
  }
}

/**
 * Spec 0004 AC-24, driven from two tabs of one origin by the runner: one tab signs up and leaves
 * the stored access token 10 seconds from expiry; each tab then watches its own client and calls
 * `account.get` at the same time.
 */
interface TabCheck {
  signUp(project: string | undefined): Promise<void>
  watch(project: string | undefined): void
  call(): Promise<string>
  events(): string[]
}

let tabClient: app.Client | undefined
const tabEvents: string[] = []

window.orvanoTabs = {
  async signUp(project) {
    const client = new app.Client({ endpoint: location.origin, project })
    await new app.Orvano(client).account.create({
      email: `tabs-${crypto.randomUUID()}@example.com`,
      password: 'correct horse battery staple',
    })
    const key = app.sessionStorageKey(project)
    const stored = JSON.parse(localStorage.getItem(key) ?? 'null') as app.AuthSession
    stored.accessTokenExpiresAt = new Date(Date.now() + 10_000).toISOString()
    localStorage.setItem(key, JSON.stringify(stored))
  },
  watch(project) {
    tabClient = new app.Client({ endpoint: location.origin, project })
    tabClient.onAuthStateChange((event) => tabEvents.push(event))
  },
  async call() {
    if (tabClient === undefined) throw new Error('watch first')
    return (await new app.Orvano(tabClient).account.get()).id
  },
  events: () => [...tabEvents],
}

/** Spec 0001, AC-4: the app entry has no key setter, and the server entry's throws in a browser. */
function keyGuard(): ScenarioResult {
  const name = 'browser: no API key can be set (AC-4)'
  const appClient: object = new app.Client({ endpoint: location.origin })
  if ('setKey' in appClient || 'apiKey' in appClient) {
    return { name, outcome: 'failed', reason: '@orvano/js exposes a key setter' }
  }
  try {
    new ServerClient({ endpoint: location.origin }).setKey('a-key')
    return { name, outcome: 'failed', reason: 'setKey did not throw in a browser' }
  } catch {
    return { name, outcome: 'passed' }
  }
}

window.orvanoRunScenarios = async (scenarios, project, consoleUser) => [
  keyGuard(),
  ...(await runScenarios(
    scenarios,
    createSurface(location.origin, { browser: true, project, consoleUser }),
    projectSurfaces(location.origin, { browser: true }),
  )),
]
