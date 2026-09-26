/**
 * The Orvano SDK for apps: browsers, mobile web, and any code a user can read. It carries only
 * `client` and `both` operations and has no way to set an API key. Server code imports
 * `@orvano/js/server` instead.
 *
 * @example
 * ```ts
 * import { Client, Orvano } from '@orvano/js'
 *
 * const orvano = new Orvano(new Client({ endpoint: 'https://orvano.example.com' }))
 * const { version } = await orvano.health.get()
 * ```
 *
 * @packageDocumentation
 */
export { Client } from './runtime/client.js'
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
export * from './generated/client.js'
export type * from './generated/models.js'
export { sdkVersion } from './generated/version.js'
