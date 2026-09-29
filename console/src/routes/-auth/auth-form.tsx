import { useForm } from '@tanstack/react-form'
import { OrvanoError } from '@orvano/console-client'
import { useState, type ReactNode } from 'react'
import { z } from 'zod'

import { Button } from '@/components/ui/button'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'

/** The sentence a sign in or sign up refusal shows, by its stable code; never raw JSON. */
export function authErrorMessage(error: unknown): string {
  if (!(error instanceof OrvanoError))
    return 'Orvano could not be reached. Check your connection and try again.'
  switch (error.code) {
    case 'invalid_credentials':
      return 'The email or password is wrong.'
    case 'rate_limited':
      return 'Too many attempts. Wait a few minutes, then try again.'
    case 'setup_token_invalid':
      return 'This setup link is not valid. Run the installer again on your server to see the right link.'
    case 'signup_closed':
      return 'Sign up on this server is by invitation. Ask an org owner for an invite link.'
    case 'invitation_not_found':
      return "This invite link isn't valid anymore. It may have been used, replaced, or revoked."
    case 'invitation_expired':
      return 'This invite expired. Ask an owner of the org for a new link.'
    case 'invitation_email_mismatch':
      return 'This invite is for another email address.'
    case 'org_not_active':
      return 'This org is being deleted.'
    case 'user_already_exists':
      return 'An account with this email already exists. Sign in instead.'
    case 'invalid_password':
      return 'Use a password of 8 to 256 characters.'
    case 'user_blocked':
      return 'This account is blocked. Ask an install admin.'
    default:
      return error.message
  }
}

const email = z
  .string()
  .trim()
  .min(1, 'Enter your email.')
  .max(320)
  .regex(/^[^\s@]+@[^\s@]+$/, 'Enter an email address.')
const password = z.string().min(1, 'Enter your password.')
const newPassword = z
  .string()
  .min(8, 'Use at least 8 characters.')
  .max(256, 'Use at most 256 characters.')

const signInSchema = z.object({ email, password }) as z.ZodType<
  Record<string, string>,
  Record<string, string>
>
const setupSchema = z.object({
  name: z.string().trim().max(256, 'Use at most 256 characters.'),
  email,
  password: newPassword,
})
const signUpSchema = setupSchema

/** What each form field needs: its name, label, type, and autocomplete hint. */
interface FieldSpec {
  name: string
  label: string
  type: 'text' | 'email' | 'password'
  autoComplete: string
  /** Shown but not editable, for an email an invite fixes (spec 0008, AC-22). */
  readOnly?: boolean
}

/** A value to start a field with, and whether it can be changed. */
export interface Prefill {
  value: string
  readOnly: boolean
}

/**
 * The shared email and password form of `/sign-in`, `/setup`, `/sign-up`, and `/invite` (spec 0004
 * AC-27, spec 0006 AC-23, spec 0008 AC-22 and AC-23):
 * TanStack Form with a Zod schema, errors under each field, and a refusal from Orvano in the alert
 * above. `onSubmit` throws to refuse; the page moves on when it resolves.
 */
function AuthForm({
  id,
  fields,
  defaults = {},
  schema,
  submit,
  failedTitle,
  errorAction,
  onSubmit,
}: {
  id: string
  fields: readonly FieldSpec[]
  defaults?: Record<string, string>
  schema: z.ZodType<Record<string, string>, Record<string, string>>
  submit: string
  failedTitle: string
  /** A button to show in the alert for some refusals, such as "Sign in instead". */
  errorAction?: ((error: unknown) => ReactNode) | undefined
  onSubmit: (values: Record<string, string>) => Promise<void>
}) {
  const [serverError, setServerError] = useState<{ message: string; action: ReactNode } | null>(
    null,
  )
  const form = useForm({
    defaultValues: Object.fromEntries(fields.map((f) => [f.name, defaults[f.name] ?? ''])),
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        await onSubmit(value)
      } catch (error) {
        setServerError({ message: authErrorMessage(error), action: errorAction?.(error) ?? null })
      }
    },
  })
  return (
    <form
      noValidate
      aria-labelledby="page-title"
      className="flex flex-col gap-(--stack)"
      onSubmit={(event) => {
        event.preventDefault()
        void form.handleSubmit()
      }}
    >
      {serverError === null ? null : (
        <FormAlert title={failedTitle}>
          {serverError.message}
          {serverError.action === null ? null : <div className="mt-2">{serverError.action}</div>}
        </FormAlert>
      )}
      {fields.map((spec) => (
        <form.Field key={spec.name} name={spec.name}>
          {(field) => {
            const invalid = field.state.meta.errors.length > 0
            const inputId = `${id}-${spec.name}`
            return (
              <Field data-invalid={invalid || undefined}>
                <FieldLabel htmlFor={inputId}>{spec.label}</FieldLabel>
                <Input
                  id={inputId}
                  name={spec.name}
                  type={spec.type}
                  autoComplete={spec.autoComplete}
                  readOnly={spec.readOnly}
                  value={field.state.value}
                  aria-invalid={invalid || undefined}
                  aria-describedby={invalid ? `${inputId}-error` : undefined}
                  onBlur={field.handleBlur}
                  onChange={(event) => {
                    field.handleChange(event.target.value)
                  }}
                />
                {invalid ? (
                  <FieldError id={`${inputId}-error`} errors={field.state.meta.errors} />
                ) : null}
              </Field>
            )
          }}
        </form.Field>
      ))}
      <form.Subscribe selector={(state) => state.isSubmitting}>
        {(submitting) => (
          <Button type="submit" disabled={submitting}>
            {submitting ? <Spinner aria-hidden /> : null}
            {submit}
          </Button>
        )}
      </form.Subscribe>
    </form>
  )
}

/** The sign in form: email and password, the email optionally filled in (spec 0008, AC-22). */
export function SignInForm({
  id = 'sign-in',
  email: emailPrefill,
  onSubmit,
}: {
  id?: string
  email?: string | undefined
  onSubmit: (values: { email: string; password: string }) => Promise<void>
}) {
  return (
    <AuthForm
      id={id}
      defaults={emailPrefill === undefined ? {} : { email: emailPrefill }}
      fields={[
        { name: 'email', label: 'Email', type: 'email', autoComplete: 'username' },
        { name: 'password', label: 'Password', type: 'password', autoComplete: 'current-password' },
      ]}
      schema={signInSchema}
      submit="Sign in"
      failedTitle="Couldn't sign in"
      onSubmit={(values) => onSubmit({ email: values.email, password: values.password })}
    />
  )
}

/** The first admin form of `/setup`: name, email, and a new password. */
export function SetupForm({
  onSubmit,
}: {
  onSubmit: (values: { name: string; email: string; password: string }) => Promise<void>
}) {
  return (
    <AuthForm
      id="setup"
      fields={[
        { name: 'name', label: 'Name', type: 'text', autoComplete: 'name' },
        { name: 'email', label: 'Email', type: 'email', autoComplete: 'username' },
        { name: 'password', label: 'Password', type: 'password', autoComplete: 'new-password' },
      ]}
      schema={setupSchema}
      submit="Create the first admin"
      failedTitle="Couldn't create the admin"
      onSubmit={(values) =>
        onSubmit({
          name: values.name,
          email: values.email,
          password: values.password,
        })
      }
    />
  )
}

/**
 * The sign up form of `/sign-up` and the invite page (spec 0008, AC-22 and AC-23): Name (optional),
 * Email, and a new password. An invite fills the email in and makes it read only.
 */
export function SignUpForm({
  id = 'sign-up',
  email: emailPrefill,
  errorAction,
  onSubmit,
}: {
  id?: string
  email?: Prefill | undefined
  errorAction?: ((error: unknown) => ReactNode) | undefined
  onSubmit: (values: { name: string; email: string; password: string }) => Promise<void>
}) {
  return (
    <AuthForm
      id={id}
      defaults={emailPrefill === undefined ? {} : { email: emailPrefill.value }}
      fields={[
        { name: 'name', label: 'Name (optional)', type: 'text', autoComplete: 'name' },
        {
          name: 'email',
          label: 'Email',
          type: 'email',
          autoComplete: 'username',
          readOnly: emailPrefill?.readOnly ?? false,
        },
        { name: 'password', label: 'Password', type: 'password', autoComplete: 'new-password' },
      ]}
      schema={signUpSchema}
      submit="Create account"
      failedTitle="Couldn't create the account"
      errorAction={errorAction}
      onSubmit={(values) =>
        onSubmit({ name: values.name, email: values.email, password: values.password })
      }
    />
  )
}
