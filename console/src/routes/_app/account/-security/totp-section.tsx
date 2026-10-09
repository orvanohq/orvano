import { OrvanoError } from '@orvano/console-client'
import type { MfaStatus, TotpSetup } from '@orvano/console-client'
import { useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { CopyButton } from '@/components/ui/copy-button'
import { FormAlert } from '@/components/ui/form-alert'
import { consoleApi } from '@/lib/console-client'
import { formatDateTime } from '@/lib/format'
import { notifyError, notifySuccess } from '@/lib/toast'
import { FactorForm } from '@/routes/-auth/factor-form'
import { SettingsSection } from '@/shell/settings-section'

import { QrCode } from './qr-code'
import { RecoveryCodesPanel } from './recovery-codes'

type Run = <T>(action: () => Promise<T>) => Promise<{ value: T } | null>

/**
 * The authenticator app section of the account Security page (spec 0013, AC-42). Off: Turn on
 * shows a QR code and the secret, and the first code turns MFA on and shows 10 recovery codes once.
 * On: when it was turned on, the codes left, Make new codes, and Turn off. Security changes go
 * through `run`, the page's step up.
 */
export function TotpSection({
  mfa,
  run,
  onChanged,
}: {
  mfa: MfaStatus
  run: Run
  onChanged: () => Promise<void>
}) {
  const [setup, setSetup] = useState<TotpSetup | null>(null)
  const [codes, setCodes] = useState<readonly string[] | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [starting, setStarting] = useState(false)

  const start = async () => {
    setStarting(true)
    setNotice(null)
    try {
      const made = await run(() => consoleApi().consoleAccount.createTotp())
      if (made !== null) setSetup(made.value)
    } catch (error) {
      notifyError("Couldn't start the authenticator app setup", error)
    } finally {
      setStarting(false)
    }
  }

  const newCodes = async () => {
    try {
      const made = await run(() => consoleApi().consoleAccount.createRecoveryCodes())
      if (made === null) return
      setCodes(made.value.codes)
      await onChanged()
    } catch (error) {
      notifyError("Couldn't make new recovery codes", error)
    }
  }

  const turnOff = async () => {
    try {
      if ((await run(() => consoleApi().consoleAccount.deleteTotp())) === null) return
      notifySuccess('Authenticator app turned off')
      await onChanged()
    } catch (error) {
      notifyError("Couldn't turn off the authenticator app", error)
    }
  }

  const body = () => {
    if (codes !== null) {
      return (
        <RecoveryCodesPanel
          codes={codes}
          onDone={() => {
            setCodes(null)
          }}
        />
      )
    }
    if (setup !== null) {
      return (
        <div className="flex flex-col gap-4">
          <h3 id="totp-setup-heading" className="text-h3">
            Scan the QR code
          </h3>
          <div className="flex flex-wrap items-start gap-6">
            <QrCode value={setup.uri} label="QR code for your authenticator app" />
            <div className="flex min-w-0 flex-col gap-2">
              <p className="text-muted-foreground">
                Can&apos;t scan it? Type this key into your app instead.
              </p>
              <code className="font-mono text-mono break-all">{setup.secret}</code>
              <CopyButton value={setup.secret} label="Copy key" />
            </div>
          </div>
          <FactorForm
            id="totp-confirm"
            labelledBy="totp-setup-heading"
            factors={['totp']}
            onAnswer={async (answer) => {
              if (!('totpCode' in answer)) return
              const confirmed = await consoleApi().consoleAccount.confirmTotp({
                code: answer.totpCode,
              })
              setSetup(null)
              setCodes(confirmed.recoveryCodes)
              notifySuccess('Authenticator app turned on')
              await onChanged()
            }}
            intercept={(error) => {
              if (!(error instanceof OrvanoError && error.code === 'totp_not_pending')) return false
              setSetup(null)
              setNotice('The setup expired after 15 minutes. Turn it on again for a new key.')
              return true
            }}
          >
            <Button
              variant="link"
              className="px-0"
              onClick={() => {
                setSetup(null)
              }}
            >
              Cancel
            </Button>
          </FactorForm>
        </div>
      )
    }
    if (!mfa.totpConfirmed) {
      return (
        <div className="flex flex-col items-start gap-(--stack)">
          {notice === null ? null : (
            <FormAlert variant="warning" title="Start again">
              {notice}
            </FormAlert>
          )}
          <p className="text-muted-foreground">
            Sign in asks for a code from your phone after your password.
          </p>
          <Button
            loading={starting}
            onClick={() => {
              void start()
            }}
          >
            Turn on
          </Button>
        </div>
      )
    }
    return (
      <div className="flex flex-col gap-4">
        <dl className="grid max-w-xl grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
          <dt className="text-muted-foreground">Status</dt>
          <dd>
            <Badge variant="status" tone="success">
              On
            </Badge>
          </dd>
          <dt className="text-muted-foreground">Turned on</dt>
          <dd>{mfa.totpConfirmedAt === null ? 'Unknown' : formatDateTime(mfa.totpConfirmedAt)}</dd>
          <dt className="text-muted-foreground">Recovery codes left</dt>
          <dd>{mfa.recoveryCodesRemaining} of 10</dd>
        </dl>
        <div className="flex flex-wrap gap-2">
          <ConfirmDialog
            trigger={<Button variant="outline">Make new codes</Button>}
            title="Make new recovery codes?"
            description="Your 10 new codes show once, and every older code stops working."
            confirmLabel="Make new codes"
            onConfirm={() => {
              void newCodes()
            }}
          />
          <ConfirmDialog
            trigger={<Button variant="outline">Turn off</Button>}
            title="Turn off the authenticator app?"
            description="Sign in will ask only for your password or a passkey. Your recovery codes stop working."
            confirmLabel="Turn off"
            destructive
            onConfirm={() => {
              void turnOff()
            }}
          />
        </div>
      </div>
    )
  }

  return (
    <SettingsSection
      title="Authenticator app"
      description="A 6 digit code from an app such as 1Password, Google Authenticator, or Microsoft Authenticator."
    >
      {body()}
    </SettingsSection>
  )
}
