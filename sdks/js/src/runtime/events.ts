import { eventRegistry } from '../generated/events.js'

/** Event names to decoders, each turning a raw JSON payload into its typed model. */
export type EventRegistry = Readonly<Record<string, (raw: unknown) => unknown>>

/**
 * Decodes a raw realtime event payload into its typed model by event name. Returns `undefined`
 * for an event this SDK does not know yet (a newer server), so you can ignore it safely.
 *
 * @param name - The event name, for example `users.created`.
 * @param raw - The payload as parsed JSON.
 * @param registry - The events to know; defaults to every event in the contract.
 */
export function decodeEvent<R extends EventRegistry, N extends keyof R & string>(
  name: N,
  raw: unknown,
  registry: R,
): ReturnType<R[N]>
export function decodeEvent(name: string, raw: unknown, registry?: EventRegistry): unknown
export function decodeEvent(
  name: string,
  raw: unknown,
  registry: EventRegistry = eventRegistry,
): unknown {
  const decode = Object.hasOwn(registry, name) ? registry[name] : undefined
  if (decode === undefined) return undefined
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    throw new TypeError(`The payload of event ${name} must be a JSON object`)
  }
  return decode(raw)
}
