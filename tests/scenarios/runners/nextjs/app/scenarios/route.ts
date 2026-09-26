// POST the scenarios as JSON; runs them in this route handler through @orvano/nextjs, with the
// session in this request's cookies.
import { MemorySessionStore } from '@orvano/js'
import { Client as ServerClient } from '@orvano/js/server'
import { Client as ConsoleClient } from '@orvano/console-client'
import { CookieSessionStore } from '@orvano/nextjs'
import { runScenarios } from '@orvano/scenarios-js'
import type { Scenario } from '@orvano/scenarios-js'
import {
  ClientSurface,
  ConsoleSurface,
  ServerSurface,
  testServerKey,
} from '@orvano/scenarios-js/surfaces'
import { cookies } from 'next/headers'
import { Client } from '@orvano/nextjs'

export const dynamic = 'force-dynamic'

export async function POST(request: Request): Promise<Response> {
  const endpoint = process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080'
  const consoleSession = process.env.ORVANO_CONSOLE_SESSION
  const scenarios = (await request.json()) as Scenario[]
  // What createServerClient builds, with the runner's test services on top.
  const session = new CookieSessionStore(await cookies())
  const results = await runScenarios(scenarios, {
    client: new ClientSurface(new Client({ endpoint, session })),
    server: new ServerSurface(new ServerClient({ endpoint, apiKey: testServerKey })),
    console:
      consoleSession === undefined
        ? undefined
        : new ConsoleSurface(
            new ConsoleClient({ endpoint, session: new MemorySessionStore(consoleSession) }),
          ),
  })
  return Response.json(results)
}
