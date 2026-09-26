/**
 * The Orvano SDK for trusted server code (Node, Deno, Bun, edge functions). It carries `server`
 * and `both` operations and accepts an API key.
 *
 * @example
 * ```ts
 * import { Client, Orvano } from '@orvano/js/server'
 *
 * const orvano = new Orvano(
 *   new Client({ endpoint: process.env.ORVANO_ENDPOINT!, apiKey: process.env.ORVANO_API_KEY! }),
 * )
 * const { version } = await orvano.health.get()
 * ```
 *
 * @packageDocumentation
 */
export { Client } from './runtime/server-client.js'
export type { ServerClientConfig } from './runtime/server-client.js'
export type { ClientConfig, HttpMethod, RequestOptions, RequestSpec } from './runtime/client.js'
export { MemorySessionStore } from './runtime/auth.js'
export type { SessionStore } from './runtime/auth.js'
export { OrvanoError } from './runtime/error.js'
export { paginate } from './runtime/pagination.js'
export type { Page } from './runtime/pagination.js'
export { decodeEvent } from './runtime/events.js'
export type { EventRegistry } from './runtime/events.js'
export { eventRegistry } from './generated/events.js'
export { ErrorCode } from './generated/errors.js'
export * from './generated/server.js'
export type * from './generated/models.js'
export { sdkVersion } from './generated/version.js'
export type { Logger } from './runtime/version.js'
