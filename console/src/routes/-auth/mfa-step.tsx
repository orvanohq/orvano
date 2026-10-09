import { OrvanoError } from '@orvano/console-client'
import type { MfaFactor } from '@orvano/console-client'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { consoleApi } from '@/lib/console-client'

import { authErrorMessage } from './auth-form'
import { passkeyAnswer, passkeyErrorMessage } from './passkeys'

type Mode = 'totp' | 'recovery'

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
  const [mode, setMode] = useState<Mode>(factors.includes('totp') ? 'totp' : 'recovery')
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'code' | 'passkey' | null>(null)

  const finish = async (work: () => Promise<unknown>, kind: 'code' | 'passkey') => {
    setBusy(kind)
    setServerError(null)
    try {
      await work()
      await onSignedIn()
    } catch (error) {
      if (error instanceof OrvanoError && error.code === 'invalid_mfa_ticket') {
        onStartOver(expired)
        return
      }
      setServerError(
        kind === 'passkey' ? passkeyErrorMessage(error) : codeErrorMessage(error, mode),
      )
    } finally {
      setBusy(null)
    }
  }

  const submit = () => {
    const value = code.trim()
    const problem = validate(value, mode)
    setFieldError(problem)
    if (problem !== null) return
    void finish(
      () =>
        consoleApi().consoleAccount.createMfaSession(
          mode === 'totp' ? { totpCode: value } : { recoveryCode: value },
        ),
      'code',
    )
  }

  const answerWithPasskey = () =>
    finish(async () => {
      const api = consoleApi()
      const passkey = await passkeyAnswer(await api.consoleAccount.createMfaPasskeyChallenge())
      await api.consoleAccount.createMfaSession({ passkey })
    }, 'passkey')

  const switchTo = (next: Mode) => {
    setMode(next)
    setCode('')
    setFieldError(null)
    setServerError(null)
  }

  const inputId = mode === 'totp' ? 'mfa-code' : 'mfa-recovery-code'
  return (
    <section aria-labelledby="mfa-heading" className="flex flex-col gap-(--stack)">
      <h2 id="mfa-heading" className="text-h3">
        Two step verification
      </h2>
      <p className="text-muted-foreground">
        {mode === 'totp'
          ? 'Enter the 6 digit code your authenticator app shows for Orvano.'
          : 'Enter one of the recovery codes you saved. Each works once.'}
      </p>
      {serverError === null ? null : <FormAlert title="Couldn't verify">{serverError}</FormAlert>}
      <form
        noValidate
        aria-labelledby="mfa-heading"
        className="flex flex-col gap-(--stack)"
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <Field data-invalid={fieldError !== null || undefined}>
          <FieldLabel htmlFor={inputId}>
            {mode === 'totp' ? 'Authentication code' : 'Recovery code'}
          </FieldLabel>
          <Input
            key={inputId}
            id={inputId}
            name={mode === 'totp' ? 'totpCode' : 'recoveryCode'}
            autoFocus
            autoComplete={mode === 'totp' ? 'one-time-code' : 'off'}
            inputMode={mode === 'totp' ? 'numeric' : 'text'}
            spellCheck={false}
            value={code}
            aria-invalid={fieldError !== null || undefined}
            aria-describedby={fieldError === null ? undefined : `${inputId}-error`}
            onChange={(event) => {
              setCode(event.target.value)
            }}
          />
          {fieldError === null ? null : (
            <FieldError id={`${inputId}-error`} errors={[{ message: fieldError }]} />
          )}
        </Field>
        <Button type="submit" loading={busy === 'code'} disabled={busy !== null}>
          Verify
        </Button>
      </form>
      <div className="flex flex-col items-start gap-2">
        {factors.includes('passkey') ? (
          <Button
            variant="outline"
            loading={busy === 'passkey'}
            disabled={busy !== null}
            onClick={() => {
              void answerWithPasskey()
            }}
          >
            Use a passkey
          </Button>
        ) : null}
        {mode === 'totp' && factors.includes('recovery_code') ? (
          <Button
            variant="link"
            className="px-0"
            onClick={() => {
              switchTo('recovery')
            }}
          >
            Use a recovery code
          </Button>
        ) : null}
        {mode === 'recovery' && factors.includes('totp') ? (
          <Button
            variant="link"
            className="px-0"
            onClick={() => {
              switchTo('totp')
            }}
          >
            Use your authenticator app
          </Button>
        ) : null}
        <Button
          variant="link"
          className="px-0"
          onClick={() => {
            onStartOver()
          }}
        >
          Start over
        </Button>
      </div>
    </section>
  )
}

/** The field's own check, before anything is sent: 6 digits, or a 10 character recovery code. */
function validate(value: string, mode: Mode): string | null {
  if (mode === 'totp') return /^\d{6}$/.test(value) ? null : 'Enter the 6 digit code.'
  return value.replace(/[\s-]/g, '').length === 10
    ? null
    : 'Enter a recovery code, like ABCDE-23456.'
}

function codeErrorMessage(error: unknown, mode: Mode): string {
  if (error instanceof OrvanoError && error.code === 'invalid_mfa_code') {
    return mode === 'totp'
      ? "That code didn't work. Enter the newest code from your app."
      : "That recovery code didn't work. It may have been used already."
  }
  return authErrorMessage(error)
}
