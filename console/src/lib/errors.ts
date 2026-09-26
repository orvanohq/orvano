import { OrvanoError } from '@orvano/console-client'

/** True for a 404 with one of the given codes (`project_not_found`, `not_found`). */
export function isNotFound(error: unknown, ...codes: string[]): boolean {
  return error instanceof OrvanoError && error.status === 404 && codes.includes(error.code)
}

/** What an error panel shows: message, code, and request ID; never raw JSON or a stack trace. */
export function describeError(error: unknown): {
  message: string
  code: string | null
  requestId: string | null
} {
  if (error instanceof OrvanoError) {
    return { message: error.message, code: error.code, requestId: error.requestId }
  }
  return {
    message:
      error instanceof Error && error.message !== '' ? error.message : 'Something went wrong.',
    code: null,
    requestId: null,
  }
}
