import { browserPasskeys, OrvanoError } from '@orvano/console-client'
import type {
  ConsoleAccount,
  PasskeyAssertionCredential,
  PasskeyChallenge,
} from '@orvano/console-client'

import { consoleApi } from '@/lib/console-client'

/** Whether this browser has passkeys; with `autofill`, whether it can offer them in the email field too. */
export function passkeysSupported(autofill = false): Promise<boolean> {
  return browserPasskeys.isSupported({ autofill }).catch(() => false)
}

/**
 * Console passkey sign in (spec 0013, AC-41): a challenge, the browser's prompt (or, with
 * `autofill`, the email field's suggestions), then the session. Orvano sets the session cookies.
 */
export async function signInWithPasskey(
  request: { autofill?: boolean; signal?: AbortSignal } = {},
): Promise<ConsoleAccount> {
  const signal = request.signal === undefined ? {} : { signal: request.signal }
  const api = consoleApi()
  const challenge = await api.consoleAccount.createPasskeyChallenge(signal)
  const credential = await browserPasskeys.get(challenge.options, {
    ...(request.autofill === true ? { mediation: 'conditional' as const } : {}),
    ...signal,
  })
  return api.consoleAccount.createPasskeySession(
    { challengeId: challenge.challengeId, credential },
    signal,
  )
}

/** A passkey's answer to a challenge Orvano made, for a sign in's second step or a step up. */
export async function passkeyAnswer(
  challenge: PasskeyChallenge,
): Promise<{ challengeId: string; credential: PasskeyAssertionCredential }> {
  return {
    challengeId: challenge.challengeId,
    credential: await browserPasskeys.get(challenge.options),
  }
}

/** True when the browser's prompt was closed or timed out, which is not an error worth a toast. */
export function passkeyCancelled(error: unknown): boolean {
  return (
    error instanceof DOMException &&
    (error.name === 'NotAllowedError' || error.name === 'AbortError')
  )
}

/** The sentence a failed passkey ceremony shows, by what went wrong. */
export function passkeyErrorMessage(error: unknown): string {
  if (passkeyCancelled(error)) return 'The passkey prompt closed. Try again, or use another way.'
  if (error instanceof OrvanoError) {
    switch (error.code) {
      case 'invalid_passkey':
        return "That passkey didn't work. Try again, or use another way."
      case 'factor_not_enabled':
        return 'Passkeys need Orvano on https with a domain name, or on localhost.'
      case 'rate_limited':
        return 'Too many attempts. Wait a few minutes, then try again.'
      case 'user_blocked':
        return 'This account is blocked. Ask an install admin.'
      default:
        return error.message
    }
  }
  return 'Orvano could not be reached. Check your connection and try again.'
}
