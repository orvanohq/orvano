import { useForm } from '@tanstack/react-form'
import { OrvanoError } from '@orvano/console-client'
import { useState } from 'react'
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
      return 'Sign up is by invitation only on this install.'
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

/** What each form field needs: its name, label, type, and autocomplete hint. */
interface FieldSpec {
  name: string
  label: string
  type: 'text' | 'email' | 'password'
  autoComplete: string
}

/**
 * The shared email and password form of `/sign-in` and `/setup` (spec 0004 AC-27, spec 0006 AC-23):
 * TanStack Form with a Zod schema, errors under each field, and a refusal from Orvano in the alert
 * above. `onSubmit` throws to refuse; the page moves on when it resolves.
 */
function AuthForm({
  id,
  fields,
  schema,
  submit,
  failedTitle,
  onSubmit,
}: {
  id: string
  fields: readonly FieldSpec[]
  schema: z.ZodType<Record<string, string>, Record<string, string>>
  submit: string
  failedTitle: string
  onSubmit: (values: Record<string, string>) => Promise<void>
}) {
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: Object.fromEntries(fields.map((f) => [f.name, ''])),
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        await onSubmit(value)
      } catch (error) {
        setServerError(authErrorMessage(error))
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
      {serverError === null ? null : <FormAlert title={failedTitle}>{serverError}</FormAlert>}
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

/** The sign in form: email and password. */
export function SignInForm({
  onSubmit,
}: {
  onSubmit: (values: { email: string; password: string }) => Promise<void>
}) {
  return (
    <AuthForm
      id="sign-in"
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
