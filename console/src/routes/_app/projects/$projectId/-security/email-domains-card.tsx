import { useState } from 'react'

import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'
import { describeError } from '@/lib/errors'
import type { AuthPolicies, UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { SwitchField } from './passwords-card'
import { PolicyCard } from './policy-card'
import { placePolicyError, readLines } from './policy-errors'

const FIELDS = ['blockedEmailDomains', 'allowedEmailDomains'] as const
type DomainField = (typeof FIELDS)[number]

const MAX_DOMAINS = 500

/**
 * The Email domains card (spec 0014, AC-8, AC-34): disposable address blocking and the blocked and
 * allowed domain lists, one domain per line. A domain also covers its subdomains. The rule checks
 * every new email (sign up, a new user, an email change), never an email a user already has.
 */
export function EmailDomainsCard({
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
  const [disposable, setDisposable] = useState(policies.blockDisposableEmails)
  const [text, setText] = useState<Record<DomainField, string>>({
    blockedEmailDomains: policies.blockedEmailDomains.join('\n'),
    allowedEmailDomains: policies.allowedEmailDomains.join('\n'),
  })
  const [errors, setErrors] = useState<Partial<Record<DomainField, string>>>({})
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setErrors({})
    setAlert(null)
    const blocked = readLines(text.blockedEmailDomains)
    const allowed = readLines(text.allowedEmailDomains)
    const tooMany: Partial<Record<DomainField, string>> = {}
    if (blocked.entries.length > MAX_DOMAINS)
      tooMany.blockedEmailDomains = 'List at most 500 domains.'
    if (allowed.entries.length > MAX_DOMAINS)
      tooMany.allowedEmailDomains = 'List at most 500 domains.'
    if (Object.keys(tooMany).length > 0) {
      setErrors(tooMany)
      return
    }
    setSaving(true)
    try {
      await onSave({
        blockDisposableEmails: disposable,
        blockedEmailDomains: blocked.entries,
        allowedEmailDomains: allowed.entries,
      })
    } catch (error) {
      const placed = placePolicyError(error, FIELDS, {
        blockedEmailDomains: blocked,
        allowedEmailDomains: allowed,
      })
      if (placed === null) setAlert(describeError(error).message)
      else setErrors({ [placed.field]: placed.message })
    } finally {
      setSaving(false)
    }
  }

  const list = (
    field: DomainField,
    id: string,
    label: string,
    hint: string,
    placeholder: string,
  ) => {
    const error = errors[field]
    return (
      <Field data-invalid={error === undefined ? undefined : true}>
        <FieldLabel htmlFor={id}>{label}</FieldLabel>
        <Textarea
          id={id}
          className="font-mono"
          rows={4}
          spellCheck={false}
          placeholder={placeholder}
          disabled={readOnly}
          value={text[field]}
          aria-invalid={error === undefined ? undefined : true}
          aria-describedby={error === undefined ? `${id}-hint` : `${id}-hint ${id}-error`}
          onChange={(event) => {
            const value = event.target.value
            setText((current) => ({ ...current, [field]: value }))
          }}
        />
        <FieldDescription id={`${id}-hint`}>{hint}</FieldDescription>
        {error === undefined ? null : (
          <FieldError id={`${id}-error`} errors={[{ message: error }]} />
        )}
      </Field>
    )
  }

  return (
    <PolicyCard
      id="email-domains"
      title="Email domains"
      description="Which email addresses new users and email changes may use. Users who already have an address keep it."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      <SwitchField
        id="block-disposable-emails"
        label="Block disposable addresses"
        hint="Refuses throwaway inboxes from a list of about 9,000 disposable email domains, kept on your server."
        checked={disposable}
        disabled={readOnly}
        onChange={setDisposable}
      />
      {list(
        'allowedEmailDomains',
        'allowed-email-domains',
        'Allowed domains',
        'One domain per line, at most 500. When you list any, only these domains (and their subdomains) can sign up, and the blocked list and disposable check are skipped.',
        'example.com',
      )}
      {list(
        'blockedEmailDomains',
        'blocked-email-domains',
        'Blocked domains',
        'One domain per line, at most 500. A domain also blocks its subdomains: example.com blocks mail.example.com.',
        'competitor.example',
      )}
    </PolicyCard>
  )
}
