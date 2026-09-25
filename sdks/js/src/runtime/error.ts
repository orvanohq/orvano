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

  constructor(status: number, code: string, message: string, requestId: string | null) {
    super(message)
    this.name = 'OrvanoError'
    this.status = status
    this.code = code
    this.requestId = requestId
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
    )
  }
}
