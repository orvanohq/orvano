// Bundled for workerd (the Cloudflare Workers runtime, the edge target). POST the scenarios as
// JSON; the response is the results.
import { runScenarios } from '../interpreter.js'
import type { Scenario } from '../interpreter.js'
import { createSurface } from '../surface.js'

interface Env {
  ORVANO_ENDPOINT: string
  ORVANO_CONSOLE_SESSION?: string
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const scenarios = (await request.json()) as Scenario[]
    const surface = createSurface(env.ORVANO_ENDPOINT, {
      consoleSession: env.ORVANO_CONSOLE_SESSION,
    })
    return Response.json(await runScenarios(scenarios, surface))
  },
}
