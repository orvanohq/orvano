import { OrvanoError } from '@orvano/console-client'
import type { MfaFactor } from '@orvano/console-client'
import { useState, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'

import { authErrorMessage } from './auth-form'
import { passkeyErrorMessage } from './passkeys'

/** One second factor to send: a code from the app, a recovery code, or a passkey to run. */
export type FactorAnswer = { totpCode: string } | { recoveryCode: string } | { passkey: true }

type Mode = 'totp' | 'recovery'

/**
 * Asks for a second factor (spec 0013, AC-41, AC-42): a 6 digit code from the authenticator app
 * (`autocomplete="one-time-code"`, numeric keyboard), a recovery code, or a passkey, whichever
 * `factors` offers. It checks the field, then hands the answer to `onAnswer`, which throws to
 * refuse; a refusal `intercept` handles (returns true) shows nothing here. `children` adds links
 * below, such as Start over. The sign in step and the step up dialog share it.
 */
export function FactorForm({
  id,
  labelledBy,
  factors,
  onAnswer,
  intercept,
  children,
}: {
  /** Prefixes the field IDs, so two forms never share one. */
  id: string
  /** The heading that names the form. */
  labelledBy: string
  factors: readonly MfaFactor[]
  onAnswer: (answer: FactorAnswer) => Promise<void>
  intercept?: (error: unknown) => boolean
  children?: ReactNode
}) {
  const [mode, setMode] = useState<Mode>(factors.includes('totp') ? 'totp' : 'recovery')
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'code' | 'passkey' | null>(null)

  const send = async (answer: FactorAnswer) => {
    const kind = 'passkey' in answer ? 'passkey' : 'code'
    setBusy(kind)
    setServerError(null)
    try {
      await onAnswer(answer)
    } catch (error) {
      if (intercept?.(error) === true) return
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
    void send(mode === 'totp' ? { totpCode: value } : { recoveryCode: value })
  }

  const switchTo = (next: Mode) => {
    setMode(next)
    setCode('')
    setFieldError(null)
    setServerError(null)
  }

  const inputId = `${id}-${mode === 'totp' ? 'code' : 'recovery-code'}`
  return (
    <div className="flex flex-col gap-(--stack)">
      <p className="text-muted-foreground">
        {mode === 'totp'
          ? 'Enter the 6 digit code your authenticator app shows for Orvano.'
          : 'Enter one of the recovery codes you saved. Each works once.'}
      </p>
      {serverError === null ? null : <FormAlert title="Couldn't verify">{serverError}</FormAlert>}
      <form
        noValidate
        aria-labelledby={labelledBy}
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
              void send({ passkey: true })
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
        {children}
      </div>
    </div>
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
