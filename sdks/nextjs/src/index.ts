/**
 * Orvano for Next.js. It wraps `@orvano/js` and has no endpoint code of its own: every operation
 * comes from the core SDK, so it can never drift from the contract.
 *
 * @example
 * ```ts
 * // app/page.tsx (a server component)
 * import { createServerClient } from '@orvano/nextjs'
 *
 * export default async function Page() {
 *   const orvano = createServerClient({ endpoint: process.env.ORVANO_ENDPOINT! })
 *   const { version } = await orvano.health.get()
 *   return <p>Orvano {version}</p>
 * }
 * ```
 *
 * @packageDocumentation
 */
import { Client, Orvano } from '@orvano/js'
import type { ClientConfig } from '@orvano/js'

export { Client, Orvano, OrvanoError } from '@orvano/js'
export type { ClientConfig } from '@orvano/js'

/**
 * An Orvano client for code that runs on the server for one request: server components, route
 * handlers, server actions, and middleware. Create one per request; never share it across
 * requests, because it will carry that request's session.
 */
export function createServerClient(config: ClientConfig): Orvano {
  return new Orvano(new Client(config))
}

const browserClients = new Map<string, Orvano>()

/**
 * An Orvano client for client components. Returns one shared instance per endpoint and project,
 * so calling it on every render is cheap.
 */
export function createBrowserClient(config: ClientConfig): Orvano {
  const key = `${config.endpoint}\n${config.project ?? ''}`
  let orvano = browserClients.get(key)
  if (orvano === undefined) {
    orvano = new Orvano(new Client(config))
    browserClients.set(key, orvano)
  }
  return orvano
}
