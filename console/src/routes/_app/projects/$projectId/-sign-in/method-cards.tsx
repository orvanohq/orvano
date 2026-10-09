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
import { Switch } from '@/components/ui/switch'
import type { AuthMethodSettings } from '@orvano/console-client'

/**
 * The Authenticator app card on the Sign in methods page (spec 0013, AC-43): whether users may turn
 * on TOTP, as a word and a switch that saves at once. Turned off, nobody is asked for a code and
 * stored factors stay. With `readOnlyReason` (a viewer) the switch is disabled and says why.
 */
export function TotpCard({
  settings,
  readOnlyReason,
  onToggle,
}: {
  settings: AuthMethodSettings
  readOnlyReason: string | undefined
  /** Saves the new value; resolves once the page holds it, throws when it failed. */
  onToggle: (enabled: boolean) => Promise<void>
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
      </CardContent>
    </Card>
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
