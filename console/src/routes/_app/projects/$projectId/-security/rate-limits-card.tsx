import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { describeError } from '@/lib/errors'
import type {
  AuthPolicies,
  AuthPolicyValues,
  UpdateAuthPoliciesRequest,
} from '@orvano/console-client'

import { PolicyCard } from './policy-card'
import { placePolicyError, readWholeNumber } from './policy-errors'

/** One editable limit: its wire name, label, unit, bounds, and how to read it from the rules. */
interface Limit {
  id: LimitId
  label: string
  unit: string
  min: number
  max: number
  of: (values: AuthPolicyValues) => number
}

type LimitId =
  | 'signInFailedPerEmailIp.limit'
  | 'signInFailedPerEmailIp.windowMinutes'
  | 'signInFailedPerIp'
  | 'signUpPerIp'
  | 'anonymousPerIp'
  | 'emailSendPerIp'

const LIMITS: readonly Limit[] = [
  {
    id: 'signInFailedPerEmailIp.limit',
    label: 'Failed sign ins per email and address',
    unit: 'Per window, from 3 to 100',
    min: 3,
    max: 100,
    of: (v) => v.signInFailedPerEmailIp.limit,
  },
  {
    id: 'signInFailedPerEmailIp.windowMinutes',
    label: 'Window for failed sign ins per email',
    unit: 'Minutes, from 1 to 1440',
    min: 1,
    max: 1440,
    of: (v) => v.signInFailedPerEmailIp.windowMinutes,
  },
  {
    id: 'signInFailedPerIp',
    label: 'Failed sign ins per address',
    unit: 'Per 15 minutes, from 10 to 10000',
    min: 10,
    max: 10000,
    of: (v) => v.signInFailedPerIp,
  },
  {
    id: 'signUpPerIp',
    label: 'Sign ups per address',
    unit: 'Per hour, from 1 to 10000',
    min: 1,
    max: 10000,
    of: (v) => v.signUpPerIp,
  },
  {
    id: 'anonymousPerIp',
    label: 'Guest sign ins per address',
    unit: 'Per hour, from 1 to 10000',
    min: 1,
    max: 10000,
    of: (v) => v.anonymousPerIp,
  },
  {
    id: 'emailSendPerIp',
    label: 'Emails sent per address',
    unit: 'Per hour, from 10 to 100000',
    min: 10,
    max: 100000,
    of: (v) => v.emailSendPerIp,
  },
]

const FIELDS = [...LIMITS.map((limit) => limit.id), 'signInFailedPerEmailIp'] as const

/** The element ID of a limit's input. */
function inputId(id: LimitId): string {
  return `limit-${id.replace('.', '-')}`
}

/**
 * The Rate limits card (spec 0014, AC-21, AC-34): the five limits a project may tune, each with
 * its default and a reset to it. Failed sign ins count per email and address, so a stranger
 * guessing elsewhere never locks the owner out; the other limits stay fixed.
 */
export function RateLimitsCard({
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
  const [values, setValues] = useState<Record<LimitId, string>>(
    () =>
      Object.fromEntries(LIMITS.map((limit) => [limit.id, String(limit.of(policies))])) as Record<
        LimitId,
        string
      >,
  )
  const [errors, setErrors] = useState<Partial<Record<string, string>>>({})
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setErrors({})
    setAlert(null)
    const read: Partial<Record<LimitId, number>> = {}
    const invalid: Partial<Record<string, string>> = {}
    for (const limit of LIMITS) {
      const value = readWholeNumber(values[limit.id], limit.min, limit.max)
      if (value === null)
        invalid[limit.id] =
          `Enter a whole number from ${String(limit.min)} to ${String(limit.max)}.`
      else read[limit.id] = value
    }
    if (Object.keys(invalid).length > 0) {
      setErrors(invalid)
      return
    }
    setSaving(true)
    try {
      await onSave({
        signInFailedPerEmailIp: {
          limit: read['signInFailedPerEmailIp.limit'] ?? policies.signInFailedPerEmailIp.limit,
          windowMinutes:
            read['signInFailedPerEmailIp.windowMinutes'] ??
            policies.signInFailedPerEmailIp.windowMinutes,
        },
        signInFailedPerIp: read.signInFailedPerIp ?? policies.signInFailedPerIp,
        signUpPerIp: read.signUpPerIp ?? policies.signUpPerIp,
        anonymousPerIp: read.anonymousPerIp ?? policies.anonymousPerIp,
        emailSendPerIp: read.emailSendPerIp ?? policies.emailSendPerIp,
      })
    } catch (error) {
      const placed = placePolicyError(error, FIELDS)
      if (placed === null) setAlert(describeError(error).message)
      else setErrors({ [placed.field]: placed.message })
    } finally {
      setSaving(false)
    }
  }

  return (
    <PolicyCard
      id="rate-limits"
      title="Rate limits"
      description="How many attempts one address gets before Orvano answers 429. Failed sign ins count per email and address, so a stranger guessing from elsewhere never locks the owner out."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      {LIMITS.map((limit) => {
        const id = inputId(limit.id)
        const error =
          errors[limit.id] ??
          (limit.id.startsWith('signInFailedPerEmailIp')
            ? errors.signInFailedPerEmailIp
            : undefined)
        const fallback = limit.of(policies.defaults)
        return (
          <Field key={limit.id} data-invalid={error === undefined ? undefined : true}>
            <FieldLabel htmlFor={id}>{limit.label}</FieldLabel>
            <div className="flex items-center gap-2">
              <Input
                id={id}
                className="w-32"
                inputMode="numeric"
                autoComplete="off"
                disabled={readOnly}
                value={values[limit.id]}
                aria-invalid={error === undefined ? undefined : true}
                aria-describedby={error === undefined ? `${id}-hint` : `${id}-hint ${id}-error`}
                onChange={(event) => {
                  setValues({ ...values, [limit.id]: event.target.value })
                }}
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabledReason={readOnlyReason}
                disabled={values[limit.id] === String(fallback)}
                aria-label={`Reset ${limit.label.toLowerCase()} to ${String(fallback)}`}
                onClick={() => {
                  setValues({ ...values, [limit.id]: String(fallback) })
                }}
              >
                Reset
              </Button>
            </div>
            <FieldDescription id={`${id}-hint`}>
              {`${limit.unit}. Default: ${String(fallback)}.`}
            </FieldDescription>
            {error === undefined ? null : (
              <FieldError id={`${id}-error`} errors={[{ message: error }]} />
            )}
          </Field>
        )
      })}
    </PolicyCard>
  )
}
