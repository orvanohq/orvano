import { useForm } from '@tanstack/react-form'
import { Link } from '@tanstack/react-router'
import { KeyRound, MailCheck, MailPlus, PenLine } from 'lucide-react'
import { useId, useState } from 'react'
import type { ReactNode } from 'react'
import { z } from 'zod'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { describeError } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'
import { OrvanoError } from '@orvano/console-client'
import type { SessionMethod, User } from '@orvano/console-client'

import { getRememberedLinkUrl, rememberLinkUrl, suggestedLinkUrls } from './link-url'

/** Whether a user's email is verified, as a word with its dot (spec 0010, AC-22). */
export function VerifiedBadge({ verified }: { verified: boolean }) {
  return (
    <Badge variant="status" tone={verified ? 'success' : 'neutral'}>
      {verified ? 'Verified' : 'Unverified'}
    </Badge>
  )
}

/** How a session began, in words (spec 0010, AC-22). */
export function sessionMethodLabel(method: SessionMethod): string {
  switch (method) {
    case 'password':
      return 'Password'
    case 'sign_up':
      return 'Sign up'
    case 'magic_link':
      return 'Magic link'
    case 'email_code':
      return 'Email code'
    case 'recovery':
      return 'Password reset'
  }
}

/** The detail line: "Verified on <date and time in the viewer's zone>" or "Not verified". */
export function verifiedLine(user: Pick<User, 'emailVerifiedAt'>): string {
  return user.emailVerifiedAt === null
    ? 'Not verified'
    : `Verified on ${formatDateTime(user.emailVerifiedAt)}`
}

/** What a failed email action shows: a message under a field, or an alert at the top. */
interface ActionError {
  field?: string
  alert?: { title: string; body: ReactNode }
}

/** Seconds as "in N seconds" or "in N minutes", for a limit's `Retry-After`. */
function retryIn(seconds: number | null): string {
  if (seconds === null || seconds <= 0) return 'in a moment'
  if (seconds < 90) return `in ${String(Math.ceil(seconds))} seconds`
  return `in ${String(Math.ceil(seconds / 60))} minutes`
}

/**
 * Maps a refusal of a send or an email change to where it shows (spec 0010, AC-23): the URL or
 * email problems under their field, the mail problems in the alert, with a link to Email settings
 * when no SMTP server is set up.
 */
function actionError(error: unknown, projectId: string): ActionError {
  if (error instanceof OrvanoError) {
    switch (error.code) {
      case 'redirect_url_not_allowed':
        return {
          field:
            "Use a URL on one of the project's web platforms (http only on localhost), or your app's own scheme for verification.",
        }
      case 'invalid_request':
        return { field: error.message }
      case 'email_already_in_use':
        return { field: 'Another user of this project has this email.' }
      case 'email_not_configured':
        return {
          alert: {
            title: 'No email server is set up.',
            body: (
              <>
                Add one in{' '}
                <Link
                  to="/projects/$projectId/email/settings"
                  params={{ projectId }}
                  className="text-link underline"
                >
                  Email settings
                </Link>
                , or ask an install admin to set up the install's.
              </>
            ),
          },
        }
      case 'email_rate_limited':
        return {
          alert: {
            title: 'This project sent too many emails this hour.',
            body: `Try again ${retryIn(error.retryAfter)}.`,
          },
        }
      case 'rate_limited':
        return {
          alert: {
            title: 'Too many emails to this address.',
            body: `Try again ${retryIn(error.retryAfter)}.`,
          },
        }
      default:
        break
    }
  }
  const { message } = describeError(error)
  return { alert: { title: 'Something went wrong.', body: message } }
}

/**
 * "Send verification email" or "Send password reset email" (spec 0010, AC-23): asks where the link
 * opens, with the project's web platforms as hints, and remembers the last URL per project.
 */
export function SendLinkDialog({
  kind,
  email,
  projectId,
  webHosts,
  disabledReason,
  onSend,
}: {
  kind: 'verification' | 'recovery'
  email: string
  projectId: string
  webHosts: readonly string[]
  disabledReason: string | undefined
  onSend: (redirectUrl: string) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [url, setUrl] = useState('')
  const [error, setError] = useState<ActionError | null>(null)
  const [sending, setSending] = useState(false)
  const id = useId()
  const fieldId = `${id}-url`
  const listId = `${id}-hosts`
  const isVerification = kind === 'verification'
  const action = isVerification ? 'Send verification email' : 'Send password reset email'
  const suggestions = suggestedLinkUrls(webHosts)

  const send = async () => {
    const trimmed = url.trim()
    if (trimmed === '') {
      setError({ field: 'Enter the URL of the page that receives the link.' })
      return
    }
    setError(null)
    setSending(true)
    try {
      await onSend(trimmed)
      rememberLinkUrl(projectId, trimmed)
      setOpen(false)
    } catch (failure) {
      setError(actionError(failure, projectId))
    } finally {
      setSending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        setError(null)
        if (next) setUrl(getRememberedLinkUrl(projectId))
      }}
    >
      <DialogTrigger render={<Button variant="outline" disabledReason={disabledReason} />}>
        {isVerification ? <MailCheck aria-hidden /> : <KeyRound aria-hidden />}
        {action}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{action}</DialogTitle>
          <DialogDescription>
            {isVerification
              ? `Orvano emails ${email} a link that verifies the address. It works for 24 hours.`
              : `Orvano emails ${email} a link to set a new password. It works for 1 hour, and signs every other device out.`}
          </DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            void send()
          }}
        >
          {error?.alert === undefined ? null : (
            <FormAlert title={error.alert.title}>{error.alert.body}</FormAlert>
          )}
          <Field data-invalid={error?.field === undefined ? undefined : true}>
            <FieldLabel htmlFor={fieldId}>Link opens at</FieldLabel>
            <Input
              id={fieldId}
              type="url"
              inputMode="url"
              autoComplete="url"
              placeholder={suggestions[0] ?? 'https://app.example.com/auth/callback'}
              list={suggestions.length === 0 ? undefined : listId}
              value={url}
              aria-invalid={error?.field === undefined ? undefined : true}
              aria-describedby={`${fieldId}-hint${error?.field === undefined ? '' : ` ${fieldId}-error`}`}
              onChange={(event) => {
                setUrl(event.target.value)
              }}
            />
            {suggestions.length === 0 ? null : (
              <datalist id={listId}>
                {suggestions.map((suggestion) => (
                  <option key={suggestion} value={suggestion} />
                ))}
              </datalist>
            )}
            <FieldDescription id={`${fieldId}-hint`}>
              {webHosts.length === 0
                ? 'This project has no web platforms yet. Add one under Platforms, then use a page on it.'
                : `Your app's page that receives the link, on a web platform: ${webHosts.join(', ')}.`}
              {isVerification ? " Your app's own scheme (com.example.app://auth) works too." : ''}
            </FieldDescription>
            {error?.field === undefined ? null : (
              <FieldError id={`${fieldId}-error`}>{error.field}</FieldError>
            )}
          </Field>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <Button type="submit" loading={sending}>
              Send email
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

const changeSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, { error: 'Enter an email.', abort: true })
    .max(320, 'Use at most 320 characters.')
    .regex(/^[^\s@]+@[^\s@]+$/, 'Enter an email address.'),
  verified: z.boolean(),
})

/**
 * "Change email" (spec 0010, AC-23): sets a new address at once, without a confirmation email. The
 * new address is unverified unless you check the box; the user's sessions stay.
 */
export function ChangeEmailDialog({
  currentEmail,
  projectId,
  disabledReason,
  onChange,
}: {
  currentEmail: string | null
  projectId: string
  disabledReason: string | undefined
  onChange: (values: { email: string; emailVerified: boolean }) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [serverError, setServerError] = useState<ActionError | null>(null)
  const id = useId()
  const form = useForm({
    defaultValues: { email: '', verified: false },
    validators: { onSubmit: changeSchema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        await onChange({ email: value.email.trim(), emailVerified: value.verified })
        setOpen(false)
        form.reset()
      } catch (error) {
        setServerError(actionError(error, projectId))
      }
    },
  })

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        setServerError(null)
        if (!next) form.reset()
      }}
    >
      <DialogTrigger render={<Button variant="outline" disabledReason={disabledReason} />}>
        <PenLine aria-hidden />
        Change email
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Change email</DialogTitle>
          <DialogDescription>
            {currentEmail === null
              ? 'The new address takes effect at once. No email is sent.'
              : `Replaces ${currentEmail} at once. No email is sent, the user stays signed in, and any link they were sent stops working.`}
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
          {serverError?.alert === undefined ? null : (
            <FormAlert title={serverError.alert.title}>{serverError.alert.body}</FormAlert>
          )}
          <form.Field name="email">
            {(field) => {
              const messages = [
                ...field.state.meta.errors.flatMap((e) => (e === undefined ? [] : [e.message])),
                ...(serverError?.field === undefined ? [] : [serverError.field]),
              ]
              const invalid = messages.length > 0
              const inputId = `${id}-email`
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={inputId}>New email</FieldLabel>
                  <Input
                    id={inputId}
                    type="email"
                    autoComplete="off"
                    value={field.state.value}
                    aria-invalid={invalid || undefined}
                    aria-describedby={invalid ? `${inputId}-error` : undefined}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.value)
                    }}
                  />
                  {invalid ? <FieldError id={`${inputId}-error`}>{messages[0]}</FieldError> : null}
                </Field>
              )
            }}
          </form.Field>
          <form.Field name="verified">
            {(field) => (
              <label className="flex items-center gap-2">
                <Checkbox
                  checked={field.state.value}
                  onCheckedChange={(checked) => {
                    field.handleChange(checked)
                  }}
                />
                Mark the new email as verified
              </label>
            )}
          </form.Field>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting}>
                  Change email
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

/**
 * The user's email and verification (spec 0010, AC-22, AC-23): the verified state and the four
 * actions. Viewers see every action disabled with the reason; the API's 403 stays the guard.
 */
export function EmailCard({
  user,
  projectId,
  webHosts,
  disabledReason,
  onSetVerified,
  onSendVerification,
  onSendRecovery,
  onChangeEmail,
}: {
  user: User
  projectId: string
  webHosts: readonly string[]
  disabledReason: string | undefined
  onSetVerified: (verified: boolean) => Promise<void>
  onSendVerification: (redirectUrl: string) => Promise<void>
  onSendRecovery: (redirectUrl: string) => Promise<void>
  onChangeEmail: (values: { email: string; emailVerified: boolean }) => Promise<void>
}) {
  const email = user.email ?? ''
  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 id="email-heading">Email and verification</h2>
        </CardTitle>
        <CardDescription>
          Apps read whether the email is verified from the user and from the access token&apos;s{' '}
          <code className="font-mono">email_verified</code> claim. Orvano never blocks an unverified
          user by itself.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[auto_1fr] items-center gap-x-6 gap-y-3">
          <dt className="text-muted-foreground">Email</dt>
          <dd className="break-all">{user.email ?? 'None'}</dd>
          <dt className="text-muted-foreground">Status</dt>
          <dd className="flex flex-wrap items-center gap-2">
            <VerifiedBadge verified={user.emailVerified} />
            <span className="text-muted-foreground">{verifiedLine(user)}</span>
          </dd>
        </dl>
      </CardContent>
      <CardFooter className="flex flex-wrap gap-2">
        {user.emailVerified ? (
          <ConfirmDialog
            trigger={
              <Button variant="outline" disabledReason={disabledReason}>
                Mark as unverified
              </Button>
            }
            title="Mark this email as unverified?"
            description="Your app sees the email as unverified until the user verifies it again. Access tokens already issued show it within 15 minutes."
            confirmLabel="Mark as unverified"
            onConfirm={() => onSetVerified(false)}
          />
        ) : (
          <ConfirmDialog
            trigger={
              <Button variant="outline" disabledReason={disabledReason}>
                <MailPlus aria-hidden />
                Mark as verified
              </Button>
            }
            title="Mark this email as verified?"
            description={`Orvano treats ${email || 'this address'} as verified from now on, and any verification link sent before stops working.`}
            confirmLabel="Mark as verified"
            onConfirm={() => onSetVerified(true)}
          />
        )}
        {user.emailVerified || user.email === null ? null : (
          <SendLinkDialog
            kind="verification"
            email={email}
            projectId={projectId}
            webHosts={webHosts}
            disabledReason={disabledReason}
            onSend={onSendVerification}
          />
        )}
        {user.email === null ? null : (
          <SendLinkDialog
            kind="recovery"
            email={email}
            projectId={projectId}
            webHosts={webHosts}
            disabledReason={disabledReason}
            onSend={onSendRecovery}
          />
        )}
        <ChangeEmailDialog
          currentEmail={user.email}
          projectId={projectId}
          disabledReason={disabledReason}
          onChange={onChangeEmail}
        />
      </CardFooter>
    </Card>
  )
}
