import { useState } from 'react'

import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { describeError } from '@/lib/errors'
import type { AuthPolicies, UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { PolicyCard } from './policy-card'
import { placePolicyError, readWholeNumber } from './policy-errors'

type SessionField =
  'accessTokenSeconds' | 'sessionIdleSeconds' | 'sessionAbsoluteSeconds' | 'maxSessionsPerUser'

/** A lifetime as the card shows it: its unit in seconds and the bounds in that unit. */
interface Lifetime {
  field: Exclude<SessionField, 'maxSessionsPerUser'>
  label: string
  unit: string
  seconds: number
  min: number
  max: number
  hint: string
}

const LIFETIMES: readonly Lifetime[] = [
  {
    field: 'accessTokenSeconds',
    label: 'Access token lifetime',
    unit: 'minutes',
    seconds: 60,
    min: 5,
    max: 60,
    hint: 'How long a token lasts before the SDK refreshes it, from 5 to 60 minutes.',
  },
  {
    field: 'sessionIdleSeconds',
    label: 'Idle session lifetime',
    unit: 'hours',
    seconds: 3600,
    min: 1,
    max: 2160,
    hint: 'A session ends after this long without a refresh, from 1 hour to 90 days (2160 hours).',
  },
  {
    field: 'sessionAbsoluteSeconds',
    label: 'Longest session lifetime',
    unit: 'days',
    seconds: 86400,
    min: 1,
    max: 365,
    hint: 'A session ends this long after sign in, however often it refreshes, from 1 to 365 days.',
  },
]

const FIELDS: readonly SessionField[] = [...LIFETIMES.map((l) => l.field), 'maxSessionsPerUser']

/**
 * The Sessions card (spec 0014, AC-25, AC-26, AC-34): the three lifetimes in minutes, hours, and
 * days, and the cap on live sessions per user. A change applies to tokens and sessions issued after
 * it; lowering the cap ends nothing until a user's next sign in.
 */
export function SessionsCard({
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
  const [values, setValues] = useState<Record<SessionField, string>>({
    accessTokenSeconds: String(policies.accessTokenSeconds / 60),
    sessionIdleSeconds: String(policies.sessionIdleSeconds / 3600),
    sessionAbsoluteSeconds: String(policies.sessionAbsoluteSeconds / 86400),
    maxSessionsPerUser:
      policies.maxSessionsPerUser === null ? '' : String(policies.maxSessionsPerUser),
  })
  const [errors, setErrors] = useState<Partial<Record<SessionField, string>>>({})
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setErrors({})
    setAlert(null)
    const invalid: Partial<Record<SessionField, string>> = {}
    const seconds: Partial<Record<SessionField, number>> = {}
    for (const lifetime of LIFETIMES) {
      const value = readWholeNumber(values[lifetime.field], lifetime.min, lifetime.max)
      if (value === null)
        invalid[lifetime.field] =
          `Enter a whole number of ${lifetime.unit} from ${String(lifetime.min)} to ${String(lifetime.max)}.`
      else seconds[lifetime.field] = value * lifetime.seconds
    }
    const capText = values.maxSessionsPerUser.trim()
    const cap = capText === '' ? null : readWholeNumber(capText, 1, 1000)
    if (capText !== '' && cap === null)
      invalid.maxSessionsPerUser = 'Enter a whole number from 1 to 1000, or leave it empty.'
    if (
      seconds.sessionIdleSeconds !== undefined &&
      seconds.sessionAbsoluteSeconds !== undefined &&
      seconds.sessionIdleSeconds > seconds.sessionAbsoluteSeconds
    )
      invalid.sessionIdleSeconds = 'The idle lifetime must be at most the longest lifetime.'
    if (Object.keys(invalid).length > 0) {
      setErrors(invalid)
      return
    }
    setSaving(true)
    try {
      await onSave({
        accessTokenSeconds: seconds.accessTokenSeconds ?? policies.accessTokenSeconds,
        sessionIdleSeconds: seconds.sessionIdleSeconds ?? policies.sessionIdleSeconds,
        sessionAbsoluteSeconds: seconds.sessionAbsoluteSeconds ?? policies.sessionAbsoluteSeconds,
        maxSessionsPerUser: cap,
      })
    } catch (error) {
      const placed = placePolicyError(error, FIELDS)
      if (placed === null) setAlert(describeError(error).message)
      else setErrors({ [placed.field]: placed.message })
    } finally {
      setSaving(false)
    }
  }

  const field = (
    name: SessionField,
    label: string,
    hint: string,
    unit: string | null,
    placeholder?: string,
  ) => {
    const id = `session-${name}`
    const error = errors[name]
    return (
      <Field key={name} data-invalid={error === undefined ? undefined : true}>
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        <div className="flex items-center gap-2">
          <Input
            id={id}
            className="w-32"
            inputMode="numeric"
            autoComplete="off"
            placeholder={placeholder}
            disabled={readOnly}
            value={values[name]}
            aria-invalid={error === undefined ? undefined : true}
            aria-describedby={error === undefined ? `${id}-hint` : `${id}-hint ${id}-error`}
            onChange={(event) => {
              setValues({ ...values, [name]: event.target.value })
            }}
          />
          {unit === null ? null : <span className="text-sm text-muted-foreground">{unit}</span>}
        </div>
        <FieldDescription id={`${id}-hint`}>{hint}</FieldDescription>
        {error === undefined ? null : (
          <FieldError id={`${id}-error`} errors={[{ message: error }]} />
        )}
      </Field>
    )
  }

  return (
    <PolicyCard
      id="sessions"
      title="Sessions"
      description="How long tokens and sessions last, and how many a user may hold at once. Changes apply to sessions that start or refresh after them."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      {LIFETIMES.map((lifetime) =>
        field(lifetime.field, lifetime.label, lifetime.hint, lifetime.unit),
      )}
      {field(
        'maxSessionsPerUser',
        'Sessions per user',
        'At most this many live sessions per user, from 1 to 1000; a new sign in ends the least recently used one. Empty means no limit.',
        null,
        'No limit',
      )}
    </PolicyCard>
  )
}
