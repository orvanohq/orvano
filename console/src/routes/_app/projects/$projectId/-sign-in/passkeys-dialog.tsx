import { useForm } from '@tanstack/react-form'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { CodeBlock } from '@/components/ui/code-block'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { describeError } from '@/lib/errors'
import type {
  AuthMethodSettings,
  Platform,
  UpdateAuthMethodSettingsRequest,
} from '@orvano/console-client'

import { androidAssetLinks, appleSiteAssociation, parseFingerprints } from './passkey-snippets'

interface Values {
  passkeysEnabled: boolean
  rpId: string
  rpName: string
  fingerprints: string
}

/**
 * The project's passkey settings (spec 0013, AC-43): the Enabled switch, the RP ID, the RP name (the
 * project name when empty), the Android certificate fingerprints, the origins a ceremony is
 * accepted from today, and the `apple-app-site-association` and `assetlinks.json` files to serve on
 * the RP ID's site. A new RP ID while passkeys can sign in asks first how many stop working, and
 * needs the new RP ID typed again (AC-2). With `readOnlyReason` (a viewer) everything is disabled.
 *
 * The form starts from `settings` when it mounts, so give each version its own `key`.
 */
export function PasskeysDialog({
  settings,
  projectName,
  platforms,
  open,
  onOpenChange,
  readOnlyReason,
  onSave,
}: {
  settings: AuthMethodSettings
  projectName: string
  platforms: readonly Platform[]
  open: boolean
  onOpenChange: (open: boolean) => void
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new settings; throws to show the error. */
  onSave: (request: UpdateAuthMethodSettingsRequest) => Promise<void>
}) {
  const readOnly = readOnlyReason !== undefined
  const [alert, setAlert] = useState<string | null>(null)
  const [pending, setPending] = useState<UpdateAuthMethodSettingsRequest | null>(null)

  const save = async (request: UpdateAuthMethodSettingsRequest) => {
    setAlert(null)
    try {
      await onSave(request)
      onOpenChange(false)
    } catch (error) {
      setAlert(describeError(error).message)
    }
  }

  const form = useForm({
    defaultValues: {
      passkeysEnabled: settings.passkeysEnabled,
      rpId: settings.rpId ?? '',
      rpName: settings.rpName ?? '',
      fingerprints: settings.androidCertFingerprints.join('\n'),
    } satisfies Values,
    onSubmit: async ({ value }) => {
      const rpId = value.rpId.trim() === '' ? null : value.rpId.trim().toLowerCase()
      const request: UpdateAuthMethodSettingsRequest = {
        passkeysEnabled: value.passkeysEnabled,
        rpId,
        rpName: value.rpName.trim() === '' ? null : value.rpName.trim(),
        androidCertFingerprints: parseFingerprints(value.fingerprints),
      }
      // AC-2: a new RP ID turns the current passkeys inactive, so ask before sending it.
      if (
        rpId !== null &&
        rpId !== settings.rpId &&
        settings.rpId !== null &&
        settings.activePasskeyCount > 0
      ) {
        setPending(request)
        return
      }
      await save(request)
    },
  })

  const apple = appleSiteAssociation(platforms)
  const android = androidAssetLinks(platforms, settings.androidCertFingerprints)
  const count = settings.activePasskeyCount

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Passkeys</DialogTitle>
          <DialogDescription>
            Users sign in with a passkey their phone, browser, or password manager keeps. Passkeys
            belong to one domain, the RP ID: your site and your apps must be on it.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          noValidate
          onSubmit={(event) => {
            event.preventDefault()
            void form.handleSubmit()
          }}
        >
          {readOnlyReason === undefined ? null : (
            <p className="text-small text-muted-foreground">{readOnlyReason}</p>
          )}
          {alert === null ? null : <FormAlert title="Couldn't save">{alert}</FormAlert>}
          <form.Field name="passkeysEnabled">
            {(field) => (
              <Field>
                <div className="flex items-center gap-3">
                  <Switch
                    id="passkeys-enabled"
                    checked={field.state.value}
                    disabled={readOnly}
                    onCheckedChange={(checked) => {
                      field.handleChange(checked)
                    }}
                  />
                  <FieldLabel htmlFor="passkeys-enabled">Enabled</FieldLabel>
                </div>
              </Field>
            )}
          </form.Field>
          <form.Field name="rpId">
            {(field) => (
              <Field>
                <FieldLabel htmlFor="passkeys-rp-id">RP ID</FieldLabel>
                <Input
                  id="passkeys-rp-id"
                  autoComplete="off"
                  spellCheck={false}
                  className="font-mono"
                  placeholder="example.com"
                  disabled={readOnly}
                  value={field.state.value}
                  aria-describedby="passkeys-rp-id-hint"
                  onBlur={field.handleBlur}
                  onChange={(event) => {
                    field.handleChange(event.target.value)
                  }}
                />
                <FieldDescription id="passkeys-rp-id-hint">
                  Your site's domain, with no scheme or path: example.com works for example.com and
                  every subdomain of it.
                </FieldDescription>
              </Field>
            )}
          </form.Field>
          <form.Field name="rpName">
            {(field) => (
              <Field>
                <FieldLabel htmlFor="passkeys-rp-name">RP name</FieldLabel>
                <Input
                  id="passkeys-rp-name"
                  autoComplete="off"
                  placeholder={projectName}
                  disabled={readOnly}
                  value={field.state.value}
                  aria-describedby="passkeys-rp-name-hint"
                  onBlur={field.handleBlur}
                  onChange={(event) => {
                    field.handleChange(event.target.value)
                  }}
                />
                <FieldDescription id="passkeys-rp-name-hint">
                  The name a passkey prompt shows. Empty uses the project name.
                </FieldDescription>
              </Field>
            )}
          </form.Field>
          <form.Field name="fingerprints">
            {(field) => (
              <Field>
                <FieldLabel htmlFor="passkeys-fingerprints">
                  Android certificate fingerprints
                </FieldLabel>
                <Textarea
                  id="passkeys-fingerprints"
                  rows={2}
                  spellCheck={false}
                  className="font-mono"
                  disabled={readOnly}
                  value={field.state.value}
                  aria-describedby="passkeys-fingerprints-hint"
                  onChange={(event) => {
                    field.handleChange(event.target.value)
                  }}
                />
                <FieldDescription id="passkeys-fingerprints-hint">
                  One per line: the SHA-256 fingerprint of each certificate that signs your Android
                  app, as AB:CD:… (at most 10).
                </FieldDescription>
              </Field>
            )}
          </form.Field>
          <section className="flex flex-col gap-2" aria-labelledby="passkeys-origins">
            <h3 id="passkeys-origins" className="text-sm font-medium">
              Accepted origins
            </h3>
            {settings.acceptedOrigins.length === 0 ? (
              <p className="text-small text-muted-foreground">
                None yet: add a web platform on the RP ID, an iOS or macOS app, or an Android
                fingerprint.
              </p>
            ) : (
              <ul className="flex flex-col gap-1 font-mono text-xs">
                {settings.acceptedOrigins.map((origin) => (
                  <li key={origin} className="break-all">
                    {origin}
                  </li>
                ))}
              </ul>
            )}
            <p className="text-small text-muted-foreground">
              From the saved settings and the project's platforms.
            </p>
          </section>
          {settings.rpId === null || (apple === null && android === null) ? null : (
            <section className="flex flex-col gap-3" aria-labelledby="passkeys-files">
              <h3 id="passkeys-files" className="text-sm font-medium">
                Files for {settings.rpId}
              </h3>
              {apple === null ? null : (
                <div className="flex flex-col gap-1.5">
                  <p className="text-small text-muted-foreground">
                    Serve at https://{settings.rpId}/.well-known/apple-app-site-association, with
                    your Apple Team ID in place of &lt;TeamID&gt;, and add the Associated Domains
                    entry webcredentials:{settings.rpId} to the app.
                  </p>
                  <CodeBlock code={apple} label="apple-app-site-association" />
                </div>
              )}
              {android === null ? null : (
                <div className="flex flex-col gap-1.5">
                  <p className="text-small text-muted-foreground">
                    Serve at https://{settings.rpId}/.well-known/assetlinks.json.
                  </p>
                  <CodeBlock code={android} label="assetlinks.json" />
                </div>
              )}
            </section>
          )}
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>
              {readOnly ? 'Close' : 'Cancel'}
            </DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting} disabledReason={readOnlyReason}>
                  Save
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(next) => {
          if (!next) setPending(null)
        }}
        title="Change the RP ID?"
        description={`${String(count)} ${count === 1 ? 'passkey' : 'passkeys'} made for ${settings.rpId ?? ''} will stop working until you change it back.`}
        confirmLabel="Change RP ID"
        destructive
        requireName={pending?.rpId ?? ''}
        onConfirm={async () => {
          if (pending === null) return
          await onSave({ ...pending, confirmRpIdChange: true })
          setPending(null)
          onOpenChange(false)
        }}
      />
    </Dialog>
  )
}
