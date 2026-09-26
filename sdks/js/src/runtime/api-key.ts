/**
 * The header an API key travels in. Temporary: the auth spec (scope row 8) replaces it, and this
 * is the only place the TS runtime names it. Only the `./server` entry imports this file.
 */
export const apiKeyHeader = 'X-Orvano-Key'

/** True in a browser page, where an API key must never be set. */
function inBrowser(): boolean {
  return typeof (globalThis as { document?: unknown }).document !== 'undefined'
}

/** Throws in a browser: API keys grant admin power and belong only in trusted server code. */
export function assertNotInBrowser(): void {
  if (inBrowser()) {
    throw new Error(
      'Orvano API keys are for trusted server code only. Never set one in a browser; use a session instead.',
    )
  }
}
