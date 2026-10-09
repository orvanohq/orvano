import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from '@tanstack/react-router'
import { OrvanoError } from '@orvano/console-client'
import type { MfaFactor } from '@orvano/console-client'
import { useCallback, useRef, useState } from 'react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { FormAlert } from '@/components/ui/form-alert'
import { consoleApi } from '@/lib/console-client'
import { FactorForm, type FactorAnswer } from '@/routes/-auth/factor-form'
import { passkeyAnswer, passkeyErrorMessage } from '@/routes/-auth/passkeys'

/** What a refused security change needs first: a second factor, or a recent sign in. */
export type StepUpNeed = 'mfa' | 'reauth'

/** The step up a 403 asks for, or null when the error is something else (spec 0013, AC-17, AC-18). */
export function stepUpNeed(error: unknown): StepUpNeed | null {
  if (!(error instanceof OrvanoError) || error.status !== 403) return null
  if (error.code === 'mfa_verification_required') return 'mfa'
  if (error.code === 'reauthentication_required') return 'reauth'
  return null
}

/** What the account can prove itself with now, for the dialog to offer. */
export interface StepUpFactors {
  /** The second factors of an account with MFA on: `totp`, `recovery_code`, `passkey`. */
  factors: readonly MfaFactor[]
  /** Whether the account has a passkey that can sign in now, and this browser can use it. */
  hasPasskey: boolean
}

interface Pending {
  need: StepUpNeed
  settle: (verified: boolean) => void
}

/**
 * Runs a security change with step up (spec 0013, AC-42): when Orvano answers 403
 * `mfa_verification_required` or `reauthentication_required`, `run` opens the dialog, and once you
 * pass it repeats the change once. It resolves to `{ value }` when the change went through, or null
 * when you closed the dialog; any other refusal throws as usual. Render `dialog` once on the page.
 */
export function useStepUp(proof: StepUpFactors) {
  const [pending, setPending] = useState<Pending | null>(null)
  const pendingRef = useRef<Pending | null>(null)

  const run = useCallback(async <T,>(action: () => Promise<T>): Promise<{ value: T } | null> => {
    try {
      return { value: await action() }
    } catch (error) {
      const need = stepUpNeed(error)
      if (need === null) throw error
      const verified = await new Promise<boolean>((settle) => {
        const next = { need, settle }
        pendingRef.current = next
        setPending(next)
      })
      pendingRef.current = null
      setPending(null)
      if (!verified) return null
      return { value: await action() }
    }
  }, [])

  const dialog =
    pending === null ? null : (
      <StepUpDialog
        need={pending.need}
        proof={proof}
        onSettle={(verified) => {
          pendingRef.current?.settle(verified)
        }}
      />
    )
  return { run, dialog }
}

/**
 * The step up dialog. With MFA on it asks for a code, a recovery code, or a passkey
 * (`consoleAccount.verifyMfa`). Without MFA and with an old session it offers a passkey when the
 * account has one, else Sign in again, which signs out and comes back to this page afterwards.
 */
function StepUpDialog({
  need,
  proof,
  onSettle,
}: {
  need: StepUpNeed
  proof: StepUpFactors
  onSettle: (verified: boolean) => void
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const verify = async (answer: FactorAnswer) => {
    const api = consoleApi().consoleAccount
    if ('passkey' in answer) {
      await api.verifyMfa({
        passkey: await passkeyAnswer(await api.createStepUpPasskeyChallenge()),
      })
    } else {
      await api.verifyMfa(answer)
    }
    onSettle(true)
  }

  const signInAgain = async () => {
    setBusy(true)
    try {
      await consoleApi().consoleAccount.deleteSession()
    } catch {
      // The session may be gone already; sign in either way.
    }
    await navigate({ to: '/sign-in', search: { redirect: '/account/security' }, replace: true })
    queryClient.clear()
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) onSettle(false)
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle id="step-up-title">Confirm it&apos;s you</DialogTitle>
          <DialogDescription>
            {need === 'mfa'
              ? 'This change needs a second factor from the last 10 minutes.'
              : 'This change needs a sign in from the last 10 minutes.'}
          </DialogDescription>
        </DialogHeader>
        {need === 'mfa' ? (
          <FactorForm
            id="step-up"
            labelledBy="step-up-title"
            factors={proof.factors}
            onAnswer={verify}
          />
        ) : (
          <div className="flex flex-col items-start gap-(--stack)">
            {error === null ? null : <FormAlert title="Couldn't verify">{error}</FormAlert>}
            {proof.hasPasskey ? (
              <Button
                loading={busy}
                onClick={() => {
                  setBusy(true)
                  setError(null)
                  verify({ passkey: true })
                    .catch((failure: unknown) => {
                      setError(passkeyErrorMessage(failure))
                    })
                    .finally(() => {
                      setBusy(false)
                    })
                }}
              >
                Use a passkey
              </Button>
            ) : (
              <Button
                loading={busy}
                onClick={() => {
                  void signInAgain()
                }}
              >
                Sign in again
              </Button>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
