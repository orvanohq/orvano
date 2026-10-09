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
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { consoleApi } from '@/lib/console-client'
import { FactorForm, type FactorAnswer } from '@/routes/-auth/factor-form'
import { passkeyAnswer, passkeyErrorMessage } from '@/routes/-auth/passkeys'

/** What a refused security change needs first: a second factor, a recent sign in, or the password. */
export type StepUpNeed = 'mfa' | 'reauth' | 'password'

/**
 * The step up a refusal asks for, or null when the error is something else (spec 0013, AC-17,
 * AC-18): 403 `mfa_verification_required` or `reauthentication_required`, or 401
 * `invalid_credentials` from a change that adds a way to sign in and needs the password.
 */
export function stepUpNeed(error: unknown): StepUpNeed | null {
  if (!(error instanceof OrvanoError)) return null
  if (error.status === 401 && error.code === 'invalid_credentials') return 'password'
  if (error.status !== 403) return null
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

/** What the dialog settled with: passed (with the password, when it asked for one), or closed. */
type StepUpResult = { password?: string } | null

interface Pending {
  need: StepUpNeed
  /** Whether the password sent last time was wrong. */
  wrongPassword: boolean
  settle: (result: StepUpResult) => void
}

/**
 * Runs a security change with step up (spec 0013, AC-42): when Orvano answers 403
 * `mfa_verification_required` or `reauthentication_required`, `run` opens the dialog, and once you
 * pass it repeats the change once. A change that adds a way to sign in takes the password: on 401
 * `invalid_credentials` the dialog asks for it and the change runs again with it, until it is right.
 * It resolves to `{ value }` when the change went through, or null when you closed the dialog; any
 * other refusal throws as usual. Render `dialog` once on the page.
 */
export function useStepUp(proof: StepUpFactors) {
  const [pending, setPending] = useState<Pending | null>(null)
  const pendingRef = useRef<Pending | null>(null)

  const ask = useCallback(
    (need: StepUpNeed, wrongPassword: boolean) =>
      new Promise<StepUpResult>((settle) => {
        const next = { need, wrongPassword, settle }
        pendingRef.current = next
        setPending(next)
      }).finally(() => {
        pendingRef.current = null
        setPending(null)
      }),
    [],
  )

  const run = useCallback(
    async <T,>(action: (password?: string) => Promise<T>): Promise<{ value: T } | null> => {
      // A second factor or a fresh sign in is asked for once; the password until it is right.
      const attempt = async (
        password: string | undefined,
        stepped: boolean,
      ): Promise<{ value: T } | null> => {
        try {
          return { value: await action(password) }
        } catch (error) {
          const need = stepUpNeed(error)
          if (need === null || (need !== 'password' && stepped)) throw error
          const result = await ask(need, need === 'password' && password !== undefined)
          if (result === null) return null
          return need === 'password' ? attempt(result.password, stepped) : attempt(password, true)
        }
      }
      return attempt(undefined, false)
    },
    [ask],
  )

  const dialog =
    pending === null ? null : (
      <StepUpDialog
        need={pending.need}
        wrongPassword={pending.wrongPassword}
        proof={proof}
        onSettle={(result) => {
          pendingRef.current?.settle(result)
        }}
      />
    )
  return { run, dialog }
}

/**
 * The step up dialog. With MFA on it asks for a code, a recovery code, or a passkey
 * (`consoleAccount.verifyMfa`). For a change that adds a way to sign in, it asks for the password.
 * Without MFA and with an old session it offers a passkey when the account has one, else Sign in
 * again, which signs out and comes back to this page afterwards.
 */
function StepUpDialog({
  need,
  wrongPassword,
  proof,
  onSettle,
}: {
  need: StepUpNeed
  wrongPassword: boolean
  proof: StepUpFactors
  onSettle: (result: StepUpResult) => void
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
    onSettle({})
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
        if (!open) onSettle(null)
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle id="step-up-title">Confirm it&apos;s you</DialogTitle>
          <DialogDescription>{describe(need)}</DialogDescription>
        </DialogHeader>
        {need === 'password' ? (
          <PasswordForm
            wrong={wrongPassword}
            onPassword={(password) => {
              onSettle({ password })
            }}
          />
        ) : need === 'mfa' ? (
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

function describe(need: StepUpNeed): string {
  switch (need) {
    case 'mfa':
      return 'This change needs a second factor from the last 10 minutes.'
    case 'reauth':
      return 'This change needs a sign in from the last 10 minutes.'
    case 'password':
      return 'Enter your password to add a way to sign in to your account.'
  }
}

/** Asks for the account's current password; says so when the last one sent was wrong. */
function PasswordForm({
  wrong,
  onPassword,
}: {
  wrong: boolean
  onPassword: (password: string) => void
}) {
  const [password, setPassword] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const inputId = 'step-up-password'

  return (
    <div className="flex flex-col gap-(--stack)">
      {wrong ? <FormAlert title="Couldn't verify">That password is wrong.</FormAlert> : null}
      <form
        noValidate
        aria-labelledby="step-up-title"
        className="flex flex-col gap-(--stack)"
        onSubmit={(event) => {
          event.preventDefault()
          if (password.length === 0) {
            setFieldError('Enter your password.')
            return
          }
          onPassword(password)
        }}
      >
        <Field data-invalid={fieldError !== null || undefined}>
          <FieldLabel htmlFor={inputId}>Password</FieldLabel>
          <Input
            id={inputId}
            name="password"
            type="password"
            autoFocus
            autoComplete="current-password"
            value={password}
            aria-invalid={fieldError !== null || undefined}
            aria-describedby={fieldError === null ? undefined : `${inputId}-error`}
            onChange={(event) => {
              setPassword(event.target.value)
              setFieldError(null)
            }}
          />
          {fieldError === null ? null : (
            <FieldError id={`${inputId}-error`} errors={[{ message: fieldError }]} />
          )}
        </Field>
        <Button type="submit">Continue</Button>
      </form>
    </div>
  )
}
