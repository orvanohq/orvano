import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { describeError } from '@/lib/errors'
import { OrvanoError } from '@orvano/console-client'
import type { AuthMethodSettings, UpdateAuthMethodSettingsRequest } from '@orvano/console-client'

/**
 * The Authenticator app card on the Sign in methods page (spec 0013, AC-43): whether users may turn
 * on TOTP, as a word and a switch that saves at once. Turned off, nobody is asked for a code and
 * stored factors stay. With `readOnlyReason` (a viewer) the switch is disabled and says why. It
 * also holds the Require MFA switch (spec 0014, AC-35).
 */
export function TotpCard({
  projectId,
  settings,
  readOnlyReason,
  onToggle,
  onRequireMfa,
}: {
  projectId: string
  settings: AuthMethodSettings
  readOnlyReason: string | undefined
  /** Saves the new value; resolves once the page holds it, throws when it failed. */
  onToggle: (enabled: boolean) => Promise<void>
  /** Saves `mfaRequired`; resolves once the page holds it, throws when it failed. */
  onRequireMfa: (required: boolean) => Promise<void>
}) {
  const [saving, setSaving] = useState(false)
  return (
    <Card>
      <CardHeader>
        <CardTitle>Authenticator app</CardTitle>
        <CardDescription>
          Users add a second step with a 6 digit code from an app such as 1Password or Google
          Authenticator, plus recovery codes.
        </CardDescription>
        <CardAction>
          <Badge variant="status" tone={settings.totpEnabled ? 'success' : 'neutral'}>
            {settings.totpEnabled ? 'On' : 'Off'}
          </Badge>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <div className="flex items-center gap-3">
          <Switch
            id="totp-enabled"
            checked={settings.totpEnabled}
            disabled={readOnlyReason !== undefined || saving}
            aria-describedby="totp-enabled-hint"
            onCheckedChange={(checked) => {
              setSaving(true)
              void onToggle(checked).finally(() => {
                setSaving(false)
              })
            }}
          />
          <label className="text-sm font-medium" htmlFor="totp-enabled">
            Enabled
          </label>
        </div>
        <p id="totp-enabled-hint" className="text-small text-muted-foreground">
          {readOnlyReason ??
            'Turned off, nobody is asked for a code, and the apps users set up stay for later.'}
        </p>
        <RequireMfa
          projectId={projectId}
          settings={settings}
          readOnlyReason={readOnlyReason}
          onSave={onRequireMfa}
        />
      </CardContent>
    </Card>
  )
}

/**
 * Require MFA (spec 0014, AC-35): a switch that saves at once, the number of signed in users with
 * no second factor (who enroll one at their next sign in), and the reason it is disabled while
 * neither the authenticator app nor passkeys are on. Turning it on needs an email server, since
 * sign ups then verify their email.
 */
function RequireMfa({
  projectId,
  settings,
  readOnlyReason,
  onSave,
}: {
  projectId: string
  settings: AuthMethodSettings
  readOnlyReason: string | undefined
  onSave: (required: boolean) => Promise<void>
}) {
  const [saving, setSaving] = useState(false)
  const [alert, setAlert] = useState<ReactNode>(null)
  const noFactor = !settings.totpEnabled && !settings.passkeysEnabled
  const count = settings.activeUsersWithoutMfa
  const users = count === 1 ? '1 signed in user has' : `${String(count)} signed in users have`
  const hint =
    readOnlyReason ??
    (noFactor && !settings.mfaRequired
      ? 'Turn on the authenticator app or passkeys first: users need a factor they can enroll.'
      : 'Every user needs an authenticator app or a passkey before they get a session. Users with none enroll one at their next sign in, and sign ups verify their email first. Sessions that exist now keep working.')

  return (
    <div className="mt-2 flex flex-col gap-2 border-t pt-4">
      <div className="flex items-center gap-3">
        <Switch
          id="mfa-required"
          checked={settings.mfaRequired}
          disabled={readOnlyReason !== undefined || saving || (noFactor && !settings.mfaRequired)}
          aria-describedby="mfa-required-hint mfa-required-count"
          onCheckedChange={(checked) => {
            setAlert(null)
            setSaving(true)
            onSave(checked)
              .catch((error: unknown) => {
                setAlert(
                  error instanceof OrvanoError && error.code === 'email_not_configured' ? (
                    <>
                      Requiring MFA needs an email server, since sign ups verify their email. Add
                      one in{' '}
                      <Link
                        to="/projects/$projectId/email/settings"
                        params={{ projectId }}
                        className="text-link underline"
                      >
                        Email settings
                      </Link>
                      , or ask an install admin to set up the install's.
                    </>
                  ) : (
                    describeError(error).message
                  ),
                )
              })
              .finally(() => {
                setSaving(false)
              })
          }}
        />
        <label className="text-sm font-medium" htmlFor="mfa-required">
          Require MFA
        </label>
      </div>
      <p id="mfa-required-hint" className="text-small text-muted-foreground">
        {hint}
      </p>
      <p id="mfa-required-count" className="text-small text-muted-foreground">
        {count === null ? '' : `${users} no second factor yet.`}
      </p>
      {alert === null ? null : (
        <FormAlert variant="error" title="Couldn't require MFA">
          {alert}
        </FormAlert>
      )}
    </div>
  )
}

/**
 * The Passkeys card on the Sign in methods page (spec 0013, AC-43): its state, the RP ID, and how
 * many passkeys can sign in, with a button that opens the passkey settings.
 */
export function PasskeysCard({
  settings,
  onOpen,
}: {
  settings: AuthMethodSettings
  onOpen: () => void
}) {
  const count = settings.activePasskeyCount
  return (
    <Card>
      <CardHeader>
        <CardTitle>Passkeys</CardTitle>
        <CardDescription>
          {settings.rpId === null
            ? 'Not set up: passkeys need a domain (the RP ID).'
            : `On ${settings.rpId}: ${String(count)} ${count === 1 ? 'passkey' : 'passkeys'} can sign in`}
        </CardDescription>
        <CardAction>
          <Badge variant="status" tone={settings.passkeysEnabled ? 'success' : 'neutral'}>
            {settings.passkeysEnabled ? 'On' : 'Off'}
          </Badge>
        </CardAction>
      </CardHeader>
      <CardContent className="flex items-center">
        <Button
          variant="outline"
          size="sm"
          className="ml-auto"
          aria-label={`${settings.rpId === null ? 'Set up' : 'Edit'} passkeys`}
          onClick={onOpen}
        >
          {settings.rpId === null ? 'Set up' : 'Edit'}
        </Button>
      </CardContent>
    </Card>
  )
}

/**
 * The Anonymous users card on the Sign in methods page (spec 0014, AC-35): whether guests may sign
 * in with `account.createAnonymousSession`, and how many idle days a guest is kept before it is
 * deleted (1 to 365). Both save together. With `readOnlyReason` (a viewer) the controls are disabled
 * and Save says why.
 */
export function AnonymousCard({
  settings,
  readOnlyReason,
  onSave,
}: {
  settings: AuthMethodSettings
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new settings; throws to show the error. */
  onSave: (request: UpdateAuthMethodSettingsRequest) => Promise<void>
}) {
  const readOnly = readOnlyReason !== undefined
  const [enabled, setEnabled] = useState(settings.anonymousEnabled)
  const [days, setDays] = useState(String(settings.anonymousIdleDays))
  const [daysError, setDaysError] = useState<string | null>(null)
  const [alert, setAlert] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const submit = async () => {
    setDaysError(null)
    setAlert(null)
    const trimmed = days.trim()
    const idleDays = /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN
    if (!(idleDays >= 1 && idleDays <= 365)) {
      setDaysError('Enter a whole number of days from 1 to 365.')
      return
    }
    setSaving(true)
    try {
      await onSave({ anonymousEnabled: enabled, anonymousIdleDays: idleDays })
    } catch (error) {
      setAlert(describeError(error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Anonymous users</CardTitle>
        <CardDescription>
          Guests use your app before they sign up, and keep their data when they upgrade to an email
          and password or link a provider.
        </CardDescription>
        <CardAction>
          <Badge variant="status" tone={settings.anonymousEnabled ? 'success' : 'neutral'}>
            {settings.anonymousEnabled ? 'On' : 'Off'}
          </Badge>
        </CardAction>
      </CardHeader>
      <CardContent>
        <form
          className="flex flex-col gap-4"
          aria-label="Anonymous users"
          noValidate
          onSubmit={(event) => {
            event.preventDefault()
            void submit()
          }}
        >
          {alert === null ? null : <FormAlert title="Couldn't save">{alert}</FormAlert>}
          <Field>
            <div className="flex items-center gap-3">
              <Switch
                id="anonymous-enabled"
                checked={enabled}
                disabled={readOnly}
                aria-describedby="anonymous-enabled-hint"
                onCheckedChange={setEnabled}
              />
              <FieldLabel htmlFor="anonymous-enabled">Allow guest sign in</FieldLabel>
            </div>
            <FieldDescription id="anonymous-enabled-hint">
              {readOnlyReason ??
                'Turned off, no new guest can sign in. Guests who exist keep their sessions and can still upgrade.'}
            </FieldDescription>
          </Field>
          <Field data-invalid={daysError === null ? undefined : true}>
            <FieldLabel htmlFor="anonymous-idle-days">Delete idle guests after</FieldLabel>
            <div className="flex items-center gap-2">
              <Input
                id="anonymous-idle-days"
                className="w-24"
                inputMode="numeric"
                autoComplete="off"
                disabled={readOnly}
                value={days}
                aria-invalid={daysError === null ? undefined : true}
                aria-describedby={
                  daysError === null
                    ? 'anonymous-idle-days-hint'
                    : 'anonymous-idle-days-hint anonymous-idle-days-error'
                }
                onChange={(event) => {
                  setDays(event.target.value)
                }}
              />
              <span className="text-sm">days</span>
            </div>
            <FieldDescription id="anonymous-idle-days-hint">
              A guest who hasn&apos;t signed in or refreshed for this long is deleted with their
              sessions, from 1 to 365 days. Default: 30.
            </FieldDescription>
            {daysError === null ? null : (
              <FieldError id="anonymous-idle-days-error" errors={[{ message: daysError }]} />
            )}
          </Field>
          <div className="flex justify-end">
            <Button type="submit" disabledReason={readOnlyReason} loading={saving}>
              Save
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
