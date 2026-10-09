import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useState } from 'react'

import { FormAlert } from '@/components/ui/form-alert'
import { describeError } from '@/lib/errors'
import { OrvanoError } from '@orvano/console-client'
import type { AuthPolicies, UpdateAuthPoliciesRequest } from '@orvano/console-client'

import { PolicyCard } from './policy-card'
import { SwitchField } from './passwords-card'

/**
 * The Sign ups card (spec 0014, AC-11, AC-34): whether new users can sign up, and whether they
 * must verify their email first. Requiring a verified email needs an email server, and while it
 * is on, sign up answers the same whether or not the email already has an account. When the email
 * server goes away later, the card warns that sign ups fail until it is back.
 */
export function SignUpsCard({
  projectId,
  policies,
  readOnlyReason,
  onSave,
}: {
  projectId: string
  policies: AuthPolicies
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new rules; throws to show the error. */
  onSave: (request: UpdateAuthPoliciesRequest) => Promise<void>
}) {
  const readOnly = readOnlyReason !== undefined
  const [signUps, setSignUps] = useState(policies.signUpsEnabled)
  const [verified, setVerified] = useState(policies.requireVerifiedEmail)
  const [alert, setAlert] = useState<ReactNode>(null)
  const [saving, setSaving] = useState(false)
  const emailSettings = <EmailSettingsLink projectId={projectId} />

  const submit = async () => {
    setAlert(null)
    setSaving(true)
    try {
      await onSave({ signUpsEnabled: signUps, requireVerifiedEmail: verified })
    } catch (error) {
      setAlert(
        error instanceof OrvanoError && error.code === 'email_not_configured' ? (
          <>
            Requiring a verified email needs an email server. Add one in {emailSettings}, or ask an
            install admin to set up the install's.
          </>
        ) : (
          describeError(error).message
        ),
      )
    } finally {
      setSaving(false)
    }
  }

  return (
    <PolicyCard
      id="sign-ups"
      title="Sign ups"
      description="Who can create an account in your app, and what they prove first. Server keys and the console can always create users."
      readOnlyReason={readOnlyReason}
      alert={alert}
      saving={saving}
      onSubmit={() => {
        void submit()
      }}
    >
      {policies.requireVerifiedEmail && !policies.smtpAvailable ? (
        <FormAlert variant="warning" title="Sign ups are failing">
          Verified emails are required, but no email server is set up, so every sign up gets
          email_not_configured. Add one in {emailSettings}, or turn this rule off.
        </FormAlert>
      ) : null}
      <SwitchField
        id="sign-ups-enabled"
        label="Allow sign ups"
        hint="When off, only existing users sign in: sign up, guest sign in, and new users from magic links, email codes, and providers are refused with sign_up_disabled."
        checked={signUps}
        disabled={readOnly}
        onChange={setSignUps}
      />
      <SwitchField
        id="require-verified-email"
        label="Require a verified email"
        hint="New users open a link from their inbox before they can sign in with a password, and providers must vouch for the email. Sign up then answers the same whether or not the email has an account, so it never reveals who signs in here. Needs an email server."
        checked={verified}
        disabled={readOnly}
        onChange={setVerified}
      />
    </PolicyCard>
  )
}

/** A link to the project's email server settings. */
function EmailSettingsLink({ projectId }: { projectId: string }) {
  return (
    <Link
      to="/projects/$projectId/email/settings"
      params={{ projectId }}
      className="text-link underline"
    >
      Email settings
    </Link>
  )
}
