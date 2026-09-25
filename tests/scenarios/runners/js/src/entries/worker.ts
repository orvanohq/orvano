// Bundled for workerd (the Cloudflare Workers runtime, the edge target). POST the scenarios as
// JSON; the response is the results.
import { runScenarios } from '../interpreter.js'
import type { Scenario } from '../interpreter.js'
import { createSurface } from '../surface.js'

interface Env {
  ORVANO_ENDPOINT: string
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const scenarios = (await request.json()) as Scenario[]
    return Response.json(await runScenarios(scenarios, createSurface(env.ORVANO_ENDPOINT)))
  },
}
