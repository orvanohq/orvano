// Runs scenarios in the current runtime (Bun and Deno spawn this). Reads the scenarios from
// ORVANO_SCENARIOS_FILE and prints the results as one JSON line after RESULT_MARKER.
import { readFile } from 'node:fs/promises'
import process from 'node:process'
import { runScenarios } from '../interpreter.js'
import type { Scenario } from '../interpreter.js'
import { createSurface } from '../surface.js'

const file = process.env.ORVANO_SCENARIOS_FILE
const endpoint = process.env.ORVANO_ENDPOINT
if (file === undefined || endpoint === undefined) {
  throw new Error('Set ORVANO_SCENARIOS_FILE and ORVANO_ENDPOINT')
}

const scenarios = JSON.parse(await readFile(file, 'utf8')) as Scenario[]
const surface = createSurface(endpoint, { consoleSession: process.env.ORVANO_CONSOLE_SESSION })
const results = await runScenarios(scenarios, surface)
console.log('ORVANO_SCENARIO_RESULTS ' + JSON.stringify(results))
