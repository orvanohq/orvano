import { useForm } from '@tanstack/react-form'
import { ExternalLink } from 'lucide-react'
import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { CopyButton } from '@/components/ui/copy-button'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { providerInfo } from '@/auth/providers'
import { describeError } from '@/lib/errors'
import type { OAuthProviderSettings, UpdateOAuthProviderRequest } from '@orvano/console-client'

import {
  fieldsOf,
  initialValues,
  placeError,
  toRequest,
  type ProviderField,
  type SecretValue,
} from './provider-fields'

/**
 * One provider's settings (spec 0012, AC-25): the Enabled switch, its fields, write only secrets
 * that show "Set, ends in 1a2b" with Replace and Clear, the callback URL to register at the
 * provider with a copy button, and a link to the provider's docs page. A refusal the server pins on
 * a field shows under it; any other shows above the buttons. With `readOnlyReason` (a viewer)
 * everything is disabled and says why.
 *
 * The form starts from `settings` when it mounts, so give each version its own `key`.
 */
export function ProviderDialog({
  settings,
  open,
  onOpenChange,
  readOnlyReason,
  onSave,
}: {
  settings: OAuthProviderSettings
  open: boolean
  onOpenChange: (open: boolean) => void
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new settings; throws to show the error. */
  onSave: (request: UpdateOAuthProviderRequest) => Promise<void>
}) {
  const provider = settings.provider
  const info = providerInfo(provider)
  const has = fieldsOf(provider)
  const readOnly = readOnlyReason !== undefined
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<ProviderField, string>>>({})
  const [alert, setAlert] = useState<string | null>(null)
  const id = `provider-${provider}`

  const form = useForm({
    defaultValues: initialValues(settings),
    onSubmit: async ({ value }) => {
      setFieldErrors({})
      setAlert(null)
      try {
        await onSave(toRequest(provider, value))
        onOpenChange(false)
      } catch (error) {
        const placed = placeError(error)
        if (placed === null) setAlert(describeError(error).message)
        else setFieldErrors({ [placed.field]: placed.message })
      }
    },
  })

  const errorOf = (name: ProviderField): string | undefined => fieldErrors[name]

  const textField = (
    name: 'clientId' | 'appleTeamId' | 'appleKeyId' | 'microsoftTenant',
    label: string,
    hint?: string,
  ) => (
    <form.Field name={name}>
      {(field) => {
        const fieldId = `${id}-${name}`
        const error = errorOf(name)
        const described = [
          hint === undefined ? null : `${fieldId}-hint`,
          error === undefined ? null : `${fieldId}-error`,
        ]
          .filter((part) => part !== null)
          .join(' ')
        return (
          <Field data-invalid={error === undefined ? undefined : true}>
            <FieldLabel htmlFor={fieldId}>{label}</FieldLabel>
            <Input
              id={fieldId}
              autoComplete="off"
              spellCheck={false}
              className="font-mono"
              disabled={readOnly}
              value={field.state.value}
              aria-invalid={error === undefined ? undefined : true}
              aria-describedby={described === '' ? undefined : described}
              onBlur={field.handleBlur}
              onChange={(event) => {
                field.handleChange(event.target.value)
              }}
            />
            {hint === undefined ? null : (
              <FieldDescription id={`${fieldId}-hint`}>{hint}</FieldDescription>
            )}
            {error === undefined ? null : (
              <FieldError id={`${fieldId}-error`} errors={[{ message: error }]} />
            )}
          </Field>
        )
      }}
    </form.Field>
  )

  const secretField = (
    name: 'clientSecret' | 'applePrivateKey',
    label: string,
    set: boolean,
    hint: string | undefined,
    multiline: boolean,
  ) => (
    <form.Field name={name}>
      {(field) => {
        const fieldId = `${id}-${name}`
        const error = errorOf(name)
        const secret: SecretValue = field.state.value
        const setSecret = (next: SecretValue) => {
          field.handleChange(next)
        }
        const hintText =
          name === 'clientSecret' && settings.clientSecretHint !== null
            ? `Set, ends in ${settings.clientSecretHint}`
            : 'Set'
        const showInput = secret.mode === 'replace'
        return (
          <Field data-invalid={error === undefined ? undefined : true}>
            <FieldLabel htmlFor={showInput ? fieldId : `${fieldId}-state`}>{label}</FieldLabel>
            {showInput ? (
              multiline ? (
                <Textarea
                  id={fieldId}
                  rows={5}
                  spellCheck={false}
                  className="font-mono text-xs"
                  disabled={readOnly}
                  placeholder="-----BEGIN PRIVATE KEY-----"
                  value={secret.value}
                  aria-invalid={error === undefined ? undefined : true}
                  aria-describedby={error === undefined ? undefined : `${fieldId}-error`}
                  onChange={(event) => {
                    setSecret({ mode: 'replace', value: event.target.value })
                  }}
                />
              ) : (
                <Input
                  id={fieldId}
                  type="password"
                  autoComplete="new-password"
                  disabled={readOnly}
                  value={secret.value}
                  aria-invalid={error === undefined ? undefined : true}
                  aria-describedby={error === undefined ? undefined : `${fieldId}-error`}
                  onChange={(event) => {
                    setSecret({ mode: 'replace', value: event.target.value })
                  }}
                />
              )
            ) : (
              <div className="flex flex-wrap items-center gap-2">
                <span id={`${fieldId}-state`} className="text-sm">
                  {secret.mode === 'clear' ? 'Clears when you save' : hintText}
                </span>
                {readOnly ? null : (
                  <>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      onClick={() => {
                        setSecret({ mode: 'replace', value: '' })
                      }}
                    >
                      Replace
                    </Button>
                    {secret.mode === 'clear' ? (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => {
                          setSecret({ mode: 'keep', value: '' })
                        }}
                      >
                        Keep it
                      </Button>
                    ) : (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => {
                          setSecret({ mode: 'clear', value: '' })
                        }}
                      >
                        Clear
                      </Button>
                    )}
                  </>
                )}
              </div>
            )}
            {set && showInput && !readOnly ? (
              <FieldDescription>
                Leave it empty to keep the one that is set.{' '}
                <button
                  type="button"
                  className="text-link underline"
                  onClick={() => {
                    setSecret({ mode: 'keep', value: '' })
                  }}
                >
                  Keep the current one
                </button>
              </FieldDescription>
            ) : null}
            {hint === undefined ? null : <FieldDescription>{hint}</FieldDescription>}
            {error === undefined ? null : (
              <FieldError id={`${fieldId}-error`} errors={[{ message: error }]} />
            )}
          </Field>
        )
      }}
    </form.Field>
  )

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        setFieldErrors({})
        setAlert(null)
        if (!next) form.reset()
      }}
    >
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Sign in with {info.label}</DialogTitle>
          <DialogDescription>
            Create an app at {info.label}, paste the callback URL there, and copy its IDs here.{' '}
            <a
              href={info.docs}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1 text-link underline"
            >
              {info.label} setup guide
              <ExternalLink aria-hidden className="size-3.5" />
              <span className="sr-only">(opens in a new tab)</span>
            </a>
          </DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            void form.handleSubmit()
          }}
        >
          {readOnlyReason === undefined ? null : (
            <p className="text-small text-muted-foreground">{readOnlyReason}</p>
          )}
          {alert === null ? null : <FormAlert title="Couldn't save">{alert}</FormAlert>}
          <form.Field name="enabled">
            {(field) => {
              const error = errorOf('enabled')
              return (
                <Field data-invalid={error === undefined ? undefined : true}>
                  <div className="flex items-center gap-3">
                    <Switch
                      id={`${id}-enabled`}
                      checked={field.state.value}
                      disabled={readOnly}
                      aria-describedby={error === undefined ? undefined : `${id}-enabled-error`}
                      onCheckedChange={(checked) => {
                        field.handleChange(checked)
                      }}
                    />
                    <FieldLabel htmlFor={`${id}-enabled`}>Enabled</FieldLabel>
                  </div>
                  {error === undefined ? null : (
                    <FieldError id={`${id}-enabled-error`} errors={[{ message: error }]} />
                  )}
                </Field>
              )
            }}
          </form.Field>
          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium" htmlFor={`${id}-callback`}>
              Callback URL
            </label>
            <div className="flex items-center gap-2">
              <input
                className="min-w-0 flex-1 rounded-md border bg-muted px-2 py-1.5 font-mono text-xs"
                id={`${id}-callback`}
                readOnly
                value={settings.callbackUrl}
              />
              <CopyButton value={settings.callbackUrl} label="Copy callback URL" />
            </div>
            <p className="text-small text-muted-foreground">
              {provider === 'apple'
                ? 'Register it as a Return URL of the Services ID.'
                : `Register it as the redirect URI of the ${info.label} app.`}
            </p>
          </div>
          {textField(
            'clientId',
            info.clientIdLabel,
            provider === 'google'
              ? 'The web client ID. Android tokens carry it too when your app passes it as the server client ID.'
              : undefined,
          )}
          {has.secret
            ? secretField(
                'clientSecret',
                'Client secret',
                settings.clientSecretSet,
                undefined,
                false,
              )
            : null}
          {has.extra ? (
            <form.Field name="clientIdsExtra">
              {(field) => {
                const fieldId = `${id}-clientIdsExtra`
                const error = errorOf('clientIdsExtra')
                return (
                  <Field data-invalid={error === undefined ? undefined : true}>
                    <FieldLabel htmlFor={fieldId}>
                      {provider === 'apple' ? 'Bundle IDs' : 'Native client IDs'}
                    </FieldLabel>
                    <Textarea
                      id={fieldId}
                      rows={2}
                      spellCheck={false}
                      className="font-mono"
                      disabled={readOnly}
                      value={field.state.value}
                      aria-invalid={error === undefined ? undefined : true}
                      aria-describedby={`${fieldId}-hint${error === undefined ? '' : ` ${fieldId}-error`}`}
                      onChange={(event) => {
                        field.handleChange(event.target.value)
                      }}
                    />
                    <FieldDescription id={`${fieldId}-hint`}>
                      {provider === 'apple'
                        ? 'One per line: the bundle IDs of your iOS and macOS apps, for native sign in.'
                        : 'One per line: the iOS client ID, which iOS tokens carry, and any other native client IDs.'}
                    </FieldDescription>
                    {error === undefined ? null : (
                      <FieldError id={`${fieldId}-error`} errors={[{ message: error }]} />
                    )}
                  </Field>
                )
              }}
            </form.Field>
          ) : null}
          {has.apple ? (
            <>
              {textField('appleTeamId', 'Team ID')}
              {textField('appleKeyId', 'Key ID', 'The ID of the Sign in with Apple key.')}
              {secretField(
                'applePrivateKey',
                'Private key (.p8)',
                settings.applePrivateKeySet,
                'Paste the whole .p8 file. Orvano makes the client secret from it.',
                true,
              )}
            </>
          ) : null}
          {has.tenant ? (
            <>
              {textField(
                'microsoftTenant',
                'Tenant',
                'common (the default), organizations, consumers, or one tenant ID.',
              )}
              <p className="text-small text-muted-foreground">
                Emails count as verified only when the app registration sends the xms_edov optional
                claim. Turn it on, or Microsoft users get no email.
              </p>
            </>
          ) : null}
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
    </Dialog>
  )
}
