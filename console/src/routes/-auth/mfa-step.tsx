import { OrvanoError } from '@orvano/console-client'
import type { MfaFactor } from '@orvano/console-client'

import { Button } from '@/components/ui/button'
import { consoleApi } from '@/lib/console-client'

import { FactorForm, type FactorAnswer } from './factor-form'
import { passkeyAnswer } from './passkeys'

const expired = 'That sign in expired. Sign in again.'

/**
 * The second step of a console sign in (spec 0013, AC-41), shared by `/sign-in` and `/invite`: a
 * 6 digit code from the authenticator app, a passkey, or a recovery code, whichever the challenge
 * offers. The ticket travels in its own cookie, so this only sends the factor. `onSignedIn` runs
 * once Orvano set the session cookies; `onStartOver` goes back to the email and password form,
 * with a reason when the ticket ran out.
 */
export function MfaStep({
  factors,
  onSignedIn,
  onStartOver,
}: {
  factors: readonly MfaFactor[]
  onSignedIn: () => Promise<void>
  onStartOver: (reason?: string) => void
}) {
  const answer = async (factor: FactorAnswer) => {
    const api = consoleApi().consoleAccount
    if ('passkey' in factor) {
      await api.createMfaSession({
        passkey: await passkeyAnswer(await api.createMfaPasskeyChallenge()),
      })
    } else {
      await api.createMfaSession(factor)
    }
    await onSignedIn()
  }

  return (
    <section aria-labelledby="mfa-heading" className="flex flex-col gap-(--stack)">
      <h2 id="mfa-heading" className="text-h3">
        Two step verification
      </h2>
      <FactorForm
        id="mfa"
        labelledBy="mfa-heading"
        factors={factors}
        onAnswer={answer}
        intercept={(error) => {
          if (!(error instanceof OrvanoError && error.code === 'invalid_mfa_ticket')) return false
          onStartOver(expired)
          return true
        }}
      >
        <Button
          variant="link"
          className="px-0"
          onClick={() => {
            onStartOver()
          }}
        >
          Start over
        </Button>
      </FactorForm>
    </section>
  )
}
