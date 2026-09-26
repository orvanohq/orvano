/**
 * Accepts a `redirect` value only when it is a path on this origin: one leading `/`, never `//`,
 * `/\`, or a scheme. Anything else returns `undefined` (closes the open redirect, spec 0005 AC-20).
 */
export function safeRedirect(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  if (!/^\/(?![/\\])/.test(value)) return undefined
  if (/\p{Cc}/u.test(value)) return undefined
  return value
}
