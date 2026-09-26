// Bundled for Chromium. The page is served from the same origin that proxies /v1 to Orvano, so
// the SDK talks to location.origin exactly as an app on the Orvano domain would, and the browser
// sends the console cookie the runner set on that origin.
import * as app from '@orvano/js'
import { Client as ServerClient } from '@orvano/js/server'
import { runScenarios } from '../interpreter.js'
import type { Scenario, ScenarioResult } from '../interpreter.js'
import { createSurface } from '../surface.js'

declare global {
  interface Window {
    orvanoRunScenarios: (scenarios: Scenario[]) => Promise<ScenarioResult[]>
  }
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

window.orvanoRunScenarios = async (scenarios) => [
  keyGuard(),
  ...(await runScenarios(scenarios, createSurface(location.origin, { browser: true }))),
]
