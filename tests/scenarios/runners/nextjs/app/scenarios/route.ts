// POST the scenarios as JSON; runs them in this route handler through @orvano/nextjs, with the
// session in this request's cookies.
import { Client as ServerClient } from '@orvano/js/server'
import { Client as ConsoleClient } from '@orvano/console-client'
import { CookiePendingMfaStore, CookieSessionStore } from '@orvano/nextjs'
import { runScenarios } from '@orvano/scenarios-js'
import type { Scenario } from '@orvano/scenarios-js'
import {
  ClientSurface,
  ConsoleSurface,
  ServerSurface,
  consoleSignIn,
} from '@orvano/scenarios-js/surfaces'
import { cookies } from 'next/headers'
import { Client } from '@orvano/nextjs'

export const dynamic = 'force-dynamic'

export async function POST(request: Request): Promise<Response> {
  const endpoint = process.env.ORVANO_ENDPOINT ?? 'http://localhost:8080'
  const consoleEmail = process.env.ORVANO_CONSOLE_EMAIL
  const consolePassword = process.env.ORVANO_CONSOLE_PASSWORD
  const project =
    process.env.ORVANO_PROJECT === undefined ? {} : { project: process.env.ORVANO_PROJECT }
  const apiKey =
    process.env.ORVANO_API_KEY === undefined ? {} : { apiKey: process.env.ORVANO_API_KEY }
  const scenarios = (await request.json()) as Scenario[]
  // What createServerClient builds, with the runner's test services on top.
  const jar = await cookies()
  const session = new CookieSessionStore(jar)
  const mfaStore = new CookiePendingMfaStore(jar)
  const console =
    consoleEmail === undefined || consolePassword === undefined
      ? undefined
      : new ConsoleSurface(new ConsoleClient({ endpoint }))
  const results = await runScenarios(scenarios, {
    client: new ClientSurface(new Client({ endpoint, ...project, session, mfaStore })),
    server: new ServerSurface(new ServerClient({ endpoint, ...project, ...apiKey })),
    serverKey: 'apiKey' in apiKey,
    console,
    consoleReady:
      console === undefined || consoleEmail === undefined || consolePassword === undefined
        ? undefined
        : consoleSignIn(console, { email: consoleEmail, password: consolePassword }),
  })
  return Response.json(results)
}
