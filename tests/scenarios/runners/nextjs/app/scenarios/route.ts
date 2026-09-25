// POST the scenarios as JSON; runs them in this route handler through @orvano/nextjs.
import { Client as ServerClient, Orvano as ServerOrvano } from '@orvano/js/server'
import { createServerClient } from '@orvano/nextjs'
import { runScenarios } from '@orvano/scenarios-js'
import type { Scenario } from '@orvano/scenarios-js'

export const dynamic = 'force-dynamic'

export async function POST(request: Request): Promise<Response> {
  const endpoint = process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080'
  const scenarios = (await request.json()) as Scenario[]
  const results = await runScenarios(scenarios, {
    client: createServerClient({ endpoint }),
    server: new ServerOrvano(new ServerClient({ endpoint })),
  })
  return Response.json(results)
}
