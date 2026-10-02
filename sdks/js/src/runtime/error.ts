/**
 * The one error every Orvano call throws when the server answers with a failure. The server sends
 * RFC 9457 problem details; `code` is Orvano's stable error code (for example
 * `user_already_exists`).
 */
export class OrvanoError extends Error {
  /** The HTTP status code. */
  readonly status: number
  /** Orvano's stable error code, or `unknown` when the response carried none. */
  readonly code: string
  /** The request ID to quote when reporting a problem, when the server sent one. */
  readonly requestId: string | null
  /**
   * How many seconds to wait before trying again, from the `Retry-After` header (a 429 or 503);
   * null when the server sent none.
   */
  readonly retryAfter: number | null

  constructor(
    status: number,
    code: string,
    message: string,
    requestId: string | null,
    retryAfter: number | null = null,
  ) {
    super(message)
    this.name = 'OrvanoError'
    this.status = status
    this.code = code
    this.requestId = requestId
    this.retryAfter = retryAfter
  }

  /** Reads a failed response into an `OrvanoError`, whatever its body looks like. */
  static async fromResponse(response: Response): Promise<OrvanoError> {
    let problem: Record<string, unknown> = {}
    try {
      const body: unknown = await response.json()
      if (typeof body === 'object' && body !== null) problem = body as Record<string, unknown>
    } catch {
      // Not JSON (a proxy error page, for example); fall back to the status line.
    }

    const text = (key: string): string | null => {
      const value = problem[key]
      return typeof value === 'string' && value !== '' ? value : null
    }

    return new OrvanoError(
      response.status,
      text('code') ?? 'unknown',
      text('detail') ?? text('title') ?? `Request failed with status ${String(response.status)}`,
      text('requestId') ?? response.headers.get('X-Request-Id'),
      retryAfterSeconds(response.headers.get('Retry-After')),
    )
  }
}

/** `Retry-After` as seconds: a number of seconds, or an HTTP date; null when absent or unreadable. */
export function retryAfterSeconds(header: string | null): number | null {
  if (header === null || header.trim() === '') return null
  const seconds = Number(header)
  if (Number.isFinite(seconds) && seconds >= 0) return seconds
  const date = Date.parse(header)
  return Number.isNaN(date) ? null : Math.max(0, Math.ceil((date - Date.now()) / 1000))
}
