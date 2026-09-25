// Bundled for Chromium. The page is served from the same origin that proxies /v1 to Orvano, so
// the SDK talks to location.origin exactly as an app on the Orvano domain would.
import { runScenarios } from '../interpreter.js'
import type { Scenario, ScenarioResult } from '../interpreter.js'
import { createSurface } from '../surface.js'

declare global {
  interface Window {
    orvanoRunScenarios: (scenarios: Scenario[]) => Promise<ScenarioResult[]>
  }
}

window.orvanoRunScenarios = (scenarios) => runScenarios(scenarios, createSurface(location.origin))
