import { useState } from 'react'

import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'
import { describeError } from '@/lib/errors'
import type { AuthPolicies, UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { PolicyCard } from './policy-card'
import { placePolicyError, readLines } from './policy-errors'

/**
 * The App servers card (spec 0014, AC-16, AC-34): the address ranges of your own servers (a
 * Next.js app, an API) that call Orvano for your users. Only a request from one of them may name
 * the visitor's address in `X-Orvano-Client-IP`, so rate limits count each visitor instead of the
 * server.
 */
export function AppServersCard({
  policies,
  readOnlyReason,
  onSave,
}: {
  policies: AuthPolicies
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new rules; throws to show the error. */
  onSave: (request: UpdateAuthPoliciesRequest) => Promise<void>
}) {
  const [text, setText] = useState(policies.trustedServerCidrs.join('\n'))
  const [error, setError] = useState<string | null>(null)
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setError(null)
    setAlert(null)
    const lines = readLines(text)
    if (lines.entries.length > 20) {
      setError('List at most 20 address ranges.')
      return
    }
    setSaving(true)
    try {
      await onSave({ trustedServerCidrs: lines.entries })
    } catch (failure) {
      const placed = placePolicyError(failure, ['trustedServerCidrs'], {
        trustedServerCidrs: lines,
      })
      if (placed === null) setAlert(describeError(failure).message)
      else setError(placed.message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <PolicyCard
      id="app-servers"
      title="App servers"
      description="Your own servers that call Orvano for your users, such as a Next.js app. Rate limits then count each visitor, not your server."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      <Field data-invalid={error === null ? undefined : true}>
        <FieldLabel htmlFor="trusted-server-cidrs">Server addresses</FieldLabel>
        <Textarea
          id="trusted-server-cidrs"
          className="font-mono"
          rows={4}
          spellCheck={false}
          placeholder={'203.0.113.10\n198.51.100.0/24'}
          disabled={readOnlyReason !== undefined}
          value={text}
          aria-invalid={error === null ? undefined : true}
          aria-describedby={
            error === null
              ? 'trusted-server-cidrs-hint'
              : 'trusted-server-cidrs-hint trusted-server-cidrs-error'
          }
          onChange={(event) => {
            setText(event.target.value)
          }}
        />
        <FieldDescription id="trusted-server-cidrs-hint">
          One address or range per line, at most 20, no wider than /12 (IPv4) or /48 (IPv6). A
          request from one of them may name the visitor in the X-Orvano-Client-IP header, which
          @orvano/nextjs sends for you. From any other address, Orvano ignores that header.
        </FieldDescription>
        {error === null ? null : (
          <FieldError id="trusted-server-cidrs-error" errors={[{ message: error }]} />
        )}
      </Field>
    </PolicyCard>
  )
}
