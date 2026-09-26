/** One recorded request: what the SDK sent. */
export interface Sent {
  method: string
  url: string
  headers: Headers
}

/** How the fake answers one request; `hang` waits until the call is cancelled. */
export type Answer = Response | 'hang' | (() => Response)

/**
 * A fake `fetch` that answers from a list (the last answer repeats) and records every request.
 * Pass `fetch` to the client config.
 */
export function fakeFetch(...answers: Answer[]): { fetch: typeof fetch; sent: Sent[] } {
  const sent: Sent[] = []
  let next = 0
  const fetch = (input: string | URL | Request, init?: RequestInit): Promise<Response> => {
    const url = input instanceof Request ? input.url : input.toString()
    sent.push({ method: init?.method ?? 'GET', url, headers: new Headers(init?.headers) })
    const answer = answers[Math.min(next++, answers.length - 1)]
    if (answer === 'hang') {
      return new Promise((_, reject) => {
        const signal = init?.signal
        // Like real fetch: an already aborted signal rejects at once.
        if (signal?.aborted === true) {
          reject(signal.reason as Error)
          return
        }
        signal?.addEventListener('abort', () => {
          reject(signal.reason as Error)
        })
      })
    }
    if (answer === undefined) throw new Error('fakeFetch needs at least one answer')
    return Promise.resolve(typeof answer === 'function' ? answer() : answer.clone())
  }
  return { fetch, sent }
}

/** A `GET /v1/health` answer from a server running `version`. */
export function health(version = '0.0.0'): () => Response {
  return () =>
    new Response(JSON.stringify({ status: 'ok', version }), {
      headers: { 'Content-Type': 'application/json', 'X-Orvano-Version': version },
    })
}

/** A bare status, optionally with `Retry-After`. */
export function status(code: number, retryAfter?: string): () => Response {
  return () =>
    new Response(null, {
      status: code,
      headers: retryAfter === undefined ? {} : { 'Retry-After': retryAfter },
    })
}

/** A problem details answer. */
export function problem(
  code: number,
  body: Record<string, unknown>,
  requestId?: string,
): () => Response {
  return () =>
    new Response(JSON.stringify(body), {
      status: code,
      headers: {
        'Content-Type': 'application/problem+json',
        ...(requestId === undefined ? {} : { 'X-Request-Id': requestId }),
      },
    })
}
