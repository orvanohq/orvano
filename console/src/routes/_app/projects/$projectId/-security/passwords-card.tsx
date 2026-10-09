import { useState } from 'react'

import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { describeError } from '@/lib/errors'
import type { AuthPolicies, UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { PolicyCard } from './policy-card'
import { placePolicyError, readWholeNumber } from './policy-errors'

const FIELDS = ['passwordMinLength', 'passwordCommonCheck', 'passwordBreachedCheck'] as const

/**
 * The Passwords card (spec 0014, AC-34): the minimum length, the common password list, and the
 * online breached check, which sends 5 characters of a hash to `api.pwnedpasswords.com`. The rules
 * apply to new passwords only, never at sign in.
 */
export function PasswordsCard({
  policies,
  readOnlyReason,
  onSave,
}: {
  policies: AuthPolicies
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new rules; throws to show the error. */
  onSave: (request: UpdateAuthPoliciesRequest) => Promise<void>
}) {
  const readOnly = readOnlyReason !== undefined
  const [minLength, setMinLength] = useState(String(policies.passwordMinLength))
  const [common, setCommon] = useState(policies.passwordCommonCheck)
  const [breached, setBreached] = useState(policies.passwordBreachedCheck)
  const [errors, setErrors] = useState<Partial<Record<(typeof FIELDS)[number], string>>>({})
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setErrors({})
    setAlert(null)
    const length = readWholeNumber(minLength, 8, 64)
    if (length === null) {
      setErrors({ passwordMinLength: 'Enter a whole number from 8 to 64.' })
      return
    }
    setSaving(true)
    try {
      await onSave({
        passwordMinLength: length,
        passwordCommonCheck: common,
        passwordBreachedCheck: breached,
      })
    } catch (error) {
      const placed = placePolicyError(error, FIELDS)
      if (placed === null) setAlert(describeError(error).message)
      else setErrors({ [placed.field]: placed.message })
    } finally {
      setSaving(false)
    }
  }

  const lengthError = errors.passwordMinLength
  return (
    <PolicyCard
      id="passwords"
      title="Passwords"
      description="Rules for every new password: at sign up, on a change, and on a reset. Passwords people already have keep working."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      <Field data-invalid={lengthError === undefined ? undefined : true}>
        <FieldLabel htmlFor="password-min-length">Minimum length</FieldLabel>
        <Input
          id="password-min-length"
          className="w-32"
          inputMode="numeric"
          autoComplete="off"
          disabled={readOnly}
          value={minLength}
          aria-invalid={lengthError === undefined ? undefined : true}
          aria-describedby={
            lengthError === undefined
              ? 'password-min-length-hint'
              : 'password-min-length-hint password-min-length-error'
          }
          onChange={(event) => {
            setMinLength(event.target.value)
          }}
        />
        <FieldDescription id="password-min-length-hint">
          {`Characters, from 8 to 64. Default: ${String(policies.defaults.passwordMinLength)}.`}
        </FieldDescription>
        {lengthError === undefined ? null : (
          <FieldError id="password-min-length-error" errors={[{ message: lengthError }]} />
        )}
      </Field>
      <SwitchField
        id="password-common-check"
        label="Refuse common passwords"
        hint="Checks new passwords against a list of the 100,000 most common ones, kept on your server."
        checked={common}
        disabled={readOnly}
        onChange={setCommon}
      />
      <SwitchField
        id="password-breached-check"
        label="Refuse breached passwords"
        hint="Checks new passwords against Have I Been Pwned: your server sends api.pwnedpasswords.com the first 5 characters of the password's SHA-1 hash, never the password. If the service doesn't answer, the password is accepted."
        checked={breached}
        disabled={readOnly}
        onChange={setBreached}
      />
    </PolicyCard>
  )
}

/** A labelled switch with its hint, as every on and off rule of the Security page shows it. */
export function SwitchField({
  id,
  label,
  hint,
  checked,
  disabled,
  onChange,
}: {
  id: string
  label: string
  hint: string
  checked: boolean
  disabled: boolean
  onChange: (checked: boolean) => void
}) {
  return (
    <Field>
      <div className="flex items-center gap-3">
        <Switch
          id={id}
          checked={checked}
          disabled={disabled}
          aria-describedby={`${id}-hint`}
          onCheckedChange={(value) => {
            onChange(value)
          }}
        />
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
      </div>
      <FieldDescription id={`${id}-hint`}>{hint}</FieldDescription>
    </Field>
  )
}
