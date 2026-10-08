/** A JSON value as a cookie value: UTF-8, then unpadded base64url, so any text survives. */
export function encodeJsonCookie(value: unknown): string {
  // UTF-8 first: btoa throws on any character above U+00FF, such as a `next` of `/日本`.
  let binary = ''
  for (const byte of new TextEncoder().encode(JSON.stringify(value)))
    binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** The JSON object an {@link encodeJsonCookie} value holds, or null when it is missing or broken. */
export function decodeJsonCookie(value: string | undefined): Record<string, unknown> | null {
  if (value === undefined || value === '') return null
  try {
    const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/'))
    const parsed: unknown = JSON.parse(
      new TextDecoder().decode(Uint8Array.from(binary, (c) => c.charCodeAt(0))),
    )
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

/** What the `orvano_mfa` cookie holds while a sign in waits at the MFA step (spec 0013, AC-37). */
export interface MfaCookie {
  /** The `orv_mt_` ticket. */
  ticket: string
  /** The factors the challenge offers. */
  factors: string[]
  /** When the ticket stops working. */
  expiresAt: string
  /** The app path to land on once the sign in finishes. */
  next: string
}

export function encodeMfaCookie(value: MfaCookie): string {
  return encodeJsonCookie({ t: value.ticket, f: value.factors, e: value.expiresAt, n: value.next })
}

export function decodeMfaCookie(value: string | undefined): MfaCookie | null {
  const parsed = decodeJsonCookie(value)
  if (
    parsed === null ||
    typeof parsed.t !== 'string' ||
    typeof parsed.e !== 'string' ||
    typeof parsed.n !== 'string' ||
    !Array.isArray(parsed.f) ||
    !parsed.f.every((f): f is string => typeof f === 'string')
  )
    return null
  return { ticket: parsed.t, factors: parsed.f, expiresAt: parsed.e, next: parsed.n }
}
