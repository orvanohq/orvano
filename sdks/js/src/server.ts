/**
 * The Orvano SDK for trusted server code (Node, Deno, Bun, edge functions). It carries `server`
 * and `both` operations.
 *
 * @example
 * ```ts
 * import { Client, Orvano } from '@orvano/js/server'
 *
 * const orvano = new Orvano(new Client({ endpoint: process.env.ORVANO_ENDPOINT! }))
 * const { version } = await orvano.health.get()
 * ```
 *
 * @packageDocumentation
 */
export { Client } from './runtime/client.js'
export type { ClientConfig, HttpMethod, RequestSpec } from './runtime/client.js'
export { OrvanoError } from './runtime/error.js'
export * from './generated/server.js'
export type * from './generated/models.js'
export { sdkVersion } from './generated/version.js'
