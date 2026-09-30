import { useForm } from '@tanstack/react-form'
import { useState, type ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { describeError } from '@/lib/errors'
import type { EmailTestResult, SmtpSettings, SmtpSettingsInput } from '@orvano/console-client'

import {
  initialValues,
  isChanged,
  placeError,
  securityChoices,
  smtpSchema,
  toInput,
  usualPort,
  type SmtpField,
  type SmtpValues,
} from './smtp-fields'

type Action = 'save' | 'test'

/** What happened last: a test that arrived, or a failure that belongs to no single field. */
type Notice = { kind: 'sent'; to: string } | { kind: 'failed'; title: string; message: string }

/** One text input of the form: its label, hint, and how the browser should treat it. */
const textFields: Record<
  Exclude<SmtpField, 'security'>,
  { label: string; hint?: string; type?: string; inputMode?: 'numeric' | 'email'; mono?: boolean }
> = {
  host: {
    label: 'Host',
    hint: 'The server’s host name, for example smtp.example.com.',
    mono: true,
  },
  port: { label: 'Port', inputMode: 'numeric', mono: true },
  username: { label: 'Username', hint: 'Leave empty if the server needs no sign in.' },
  password: { label: 'Password', type: 'password' },
  fromEmail: {
    label: 'From email',
    hint: 'The address your users see. Use a domain this server may send for.',
    inputMode: 'email',
  },
  fromName: {
    label: 'From name',
    hint: 'Shown beside the address, for example your product’s name.',
  },
  replyTo: {
    label: 'Reply to',
    hint: 'Where replies go. Leave empty to use the From address.',
    inputMode: 'email',
  },
}

/**
 * The SMTP settings form (spec 0009, AC-1, AC-2, AC-6): Host, Port, Security, Username, Password, From
 * email, From name, and Reply to, with Send test email and Save. Save stays off until something
 * changed. A test sends the form's current values, saved or not. A field the server refuses shows its
 * message under that field; any other failure shows in the alert above the buttons.
 *
 * With `readOnlyReason` (a viewer) every field is read only, the password field is not there, and
 * the buttons say why. The form starts from `settings` when it mounts, so give each version its `key`.
 */
export function SmtpForm({
  id,
  settings,
  readOnlyReason,
  onSave,
  onTest,
  actions,
}: {
  /** Prefix for the field IDs, unique on the page. */
  id: string
  /** The stored settings, or null for an empty form. */
  settings: SmtpSettings | null
  readOnlyReason: string | undefined
  /** Saves and resolves once the page holds the new settings; throws to show the error. */
  onSave: (input: SmtpSettingsInput) => Promise<void>
  /** Sends the test email; throws to show the error. */
  onTest: (input: SmtpSettingsInput) => Promise<EmailTestResult>
  /** More buttons beside Save, for example "Stop using these settings". */
  actions?: ReactNode
}) {
  const initial = initialValues(settings)
  const readOnly = readOnlyReason !== undefined
  const [serverErrors, setServerErrors] = useState<Partial<Record<SmtpField, string>>>({})
  const [notice, setNotice] = useState<Notice | null>(null)
  const [running, setRunning] = useState<Action | null>(null)

  const form = useForm({
    defaultValues: initial,
    validators: { onSubmit: smtpSchema },
    onSubmitMeta: 'save' as Action,
    onSubmit: async ({ value, meta }) => {
      setServerErrors({})
      setNotice(null)
      setRunning(meta)
      try {
        if (meta === 'save') {
          await onSave(toInput(value))
        } else {
          const result = await onTest(toInput(value))
          setNotice({ kind: 'sent', to: result.sentTo })
        }
      } catch (error) {
        const placed = placeError(error)
        if (placed !== null) {
          setServerErrors({ [placed.field]: placed.message })
        } else {
          setNotice({
            kind: 'failed',
            title: meta === 'save' ? "Couldn't save" : "Couldn't send the test email",
            message: describeError(error).message,
          })
        }
      } finally {
        setRunning(null)
      }
    },
  })

  const textField = (name: Exclude<SmtpField, 'security'>, extra?: { placeholder?: string }) => (
    <form.Field name={name}>
      {(field) => {
        const spec = textFields[name]
        const fieldId = `${id}-${name}`
        const serverError = serverErrors[name]
        const errors =
          serverError === undefined ? field.state.meta.errors : [{ message: serverError }]
        const invalid = errors.length > 0
        const describedBy = [
          spec.hint === undefined ? undefined : `${fieldId}-hint`,
          invalid ? `${fieldId}-error` : undefined,
        ].filter((part) => part !== undefined)
        return (
          <Field data-invalid={invalid || undefined}>
            <FieldLabel htmlFor={fieldId}>{spec.label}</FieldLabel>
            <Input
              id={fieldId}
              type={spec.type ?? 'text'}
              inputMode={spec.inputMode}
              // A mail server's credentials are not the visitor's own: keep password managers out.
              autoComplete={name === 'password' ? 'new-password' : 'off'}
              autoCapitalize="none"
              spellCheck={false}
              readOnly={readOnly}
              placeholder={extra?.placeholder}
              className={spec.mono === true ? 'font-mono' : undefined}
              value={field.state.value}
              aria-invalid={invalid || undefined}
              aria-describedby={describedBy.length === 0 ? undefined : describedBy.join(' ')}
              onBlur={field.handleBlur}
              onChange={(event) => {
                field.handleChange(event.target.value)
                if (serverError !== undefined) {
                  setServerErrors((current) => ({ ...current, [name]: undefined }))
                }
              }}
            />
            {spec.hint === undefined ? null : (
              <FieldDescription id={`${fieldId}-hint`}>{spec.hint}</FieldDescription>
            )}
            {invalid ? <FieldError id={`${fieldId}-error`} errors={errors} /> : null}
          </Field>
        )
      }}
    </form.Field>
  )

  return (
    <form
      noValidate
      className="flex max-w-xl flex-col gap-(--stack)"
      onSubmit={(event) => {
        event.preventDefault()
        if (!readOnly) void form.handleSubmit('save')
      }}
    >
      <div className="grid gap-(--stack) sm:grid-cols-[1fr_8rem]">
        {textField('host')}
        {textField('port')}
      </div>
      <form.Field name="security">
        {(field) => {
          const serverError = serverErrors.security
          return (
            <Field data-invalid={serverError !== undefined || undefined}>
              <span id={`${id}-security`} className="text-sm font-medium">
                Security
              </span>
              <RadioGroup
                aria-labelledby={`${id}-security`}
                aria-describedby={serverError === undefined ? undefined : `${id}-security-error`}
                readOnly={readOnly}
                value={field.state.value}
                onValueChange={(next) => {
                  const security = next as SmtpValues['security']
                  field.handleChange(security)
                  setServerErrors((current) => ({ ...current, security: undefined }))
                  // Fills the usual port only while Port is still empty (spec 0009, Screens).
                  if (form.getFieldValue('port').trim() === '') {
                    form.setFieldValue('port', usualPort(security))
                  }
                }}
              >
                {securityChoices.map((choice) => {
                  const choiceId = `${id}-security-${choice.value}`
                  return (
                    <label
                      key={choice.value}
                      htmlFor={choiceId}
                      className="flex w-fit cursor-pointer items-center gap-2"
                    >
                      <RadioGroupItem
                        id={choiceId}
                        value={choice.value}
                        aria-labelledby={`${choiceId}-label`}
                      />
                      <span id={`${choiceId}-label`}>{choice.label}</span>
                    </label>
                  )
                })}
              </RadioGroup>
              <form.Subscribe selector={(state) => state.values.security}>
                {(security) =>
                  security === 'none' ? (
                    <FieldDescription>
                      Nothing is encrypted, so a username and password can’t be used.
                    </FieldDescription>
                  ) : null
                }
              </form.Subscribe>
              {serverError === undefined ? null : (
                <FieldError id={`${id}-security-error`} errors={[{ message: serverError }]} />
              )}
            </Field>
          )
        }}
      </form.Field>
      {textField('username')}
      {readOnly
        ? null
        : textField(
            'password',
            settings?.hasPassword === true
              ? { placeholder: 'Saved. Leave empty to keep it.' }
              : undefined,
          )}
      {textField('fromEmail')}
      {textField('fromName')}
      {textField('replyTo')}
      {notice === null ? null : notice.kind === 'sent' ? (
        <FormAlert variant="success" title="Test email sent">
          Sent to {notice.to}. Check your inbox.
        </FormAlert>
      ) : (
        <FormAlert title={notice.title}>{notice.message}</FormAlert>
      )}
      <form.Subscribe selector={(state) => state.values}>
        {(values) => (
          <div className="flex flex-wrap items-center gap-2">
            <Button
              type="submit"
              loading={running === 'save'}
              disabled={running === 'test'}
              disabledReason={
                readOnlyReason ?? (isChanged(values, initial) ? undefined : 'Nothing to save yet')
              }
            >
              Save
            </Button>
            <Button
              type="button"
              variant="outline"
              loading={running === 'test'}
              disabled={running === 'save'}
              disabledReason={readOnlyReason}
              onClick={() => {
                void form.handleSubmit('test')
              }}
            >
              Send test email
            </Button>
            {actions === undefined ? null : <div className="ml-auto">{actions}</div>}
          </div>
        )}
      </form.Subscribe>
    </form>
  )
}
