import { useForm } from '@tanstack/react-form'
import { useMutation } from '@tanstack/react-query'
import { OrvanoError } from '@orvano/console-client'
import { useEffect, useRef, useState } from 'react'
import { z } from 'zod'

import { Button } from '@/components/ui/button'
import { CodeBlock } from '@/components/ui/code-block'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel, FieldLegend, FieldSet } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'
import { describeError } from '@/lib/errors'
import { formatDate, formatFull } from '@/lib/format'
import { roleInfo } from '@/lib/roles'
import type {
  CreateInvitationRequest,
  CreatedInvitation,
  Invitation,
  OrgRole,
} from '@orvano/console-client'

import { RoleRadioGroup } from './role-radio-group'

const schema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Enter an email.')
    .max(320, 'Use at most 320 characters.')
    .regex(/^[^\s@]+@[^\s@]+$/, 'Enter an email address.'),
  role: z.custom<OrgRole>(),
})

/**
 * The dialog's one piece of state: the form, a resend in flight, or the link step. The url lives
 * only here, in this component's state, and goes when the dialog closes (spec 0008, AC-17).
 */
type Step =
  | { step: 'form' }
  | { step: 'resending'; error: string | null }
  | { step: 'link'; url: string; invitation: Invitation }

/**
 * Invite a teammate (spec 0008, AC-16 and AC-17): Email and Role, then a "Share this invite link"
 * step. With `resend`, it skips the form and makes a new link for that email and role at once
 * (AC-18). The create mutation keeps nothing (`gcTime: 0`) and is reset as soon as the url is copied
 * into this state, so the url is never in the query or mutation cache, storage, the address bar, a
 * toast, or a log. Escape, a click outside, the close button, and Done all close it.
 */
export function InviteDialog({
  open,
  onOpenChange,
  resend,
  createInvitation,
  onCreated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Resend: the email and role to invite again, skipping the form. */
  resend?: { email: string; role: OrgRole } | undefined
  /** Creates the invitation: `consoleInvitations.create` on the page, a stand in in the catalog. */
  createInvitation: (body: CreateInvitationRequest) => Promise<CreatedInvitation>
  /** Runs after each create, for example to refresh the pending invitations. */
  onCreated?: () => void
}) {
  const [current, setStep] = useState<Step>({ step: 'form' })
  // A resend has no form: until its link or its error arrives, it shows its progress.
  const step: Step =
    current.step === 'form' && resend !== undefined ? { step: 'resending', error: null } : current
  const [serverError, setServerError] = useState<string | null>(null)
  const [emailError, setEmailError] = useState<string | null>(null)
  const copyRef = useRef<HTMLButtonElement>(null)
  const create = useMutation({
    mutationFn: createInvitation,
    // With no observer after `reset()`, the mutation leaves the cache at once (AC-17).
    gcTime: 0,
  })

  /** Creates the invitation and keeps its url only in this component's state. */
  const invite = async (body: CreateInvitationRequest) => {
    try {
      const created = await create.mutateAsync(body)
      setStep({ step: 'link', url: created.url, invitation: created.invitation })
      onCreated?.()
    } finally {
      create.reset()
    }
  }

  const form = useForm({
    defaultValues: { email: '', role: 'developer' as OrgRole },
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      setEmailError(null)
      try {
        await invite({ email: value.email.trim(), role: value.role })
      } catch (error) {
        if (error instanceof OrvanoError && error.code === 'already_member') {
          setEmailError(error.message)
        } else {
          setServerError(describeError(error).message)
        }
      }
    },
  })

  // A resend starts as soon as the dialog opens with one.
  const resendEmail = open ? resend?.email : undefined
  const resendRole = open ? resend?.role : undefined
  useEffect(() => {
    if (resendEmail === undefined || resendRole === undefined) return
    invite({ email: resendEmail, role: resendRole }).catch((error: unknown) => {
      setStep({ step: 'resending', error: describeError(error).message })
    })
    // `invite` is recreated each render; the resend runs once per opening.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [resendEmail, resendRole])

  // The dialog stays open between steps, so focus moves by hand (AC-17).
  const linking = step.step === 'link'
  useEffect(() => {
    if (linking) copyRef.current?.focus()
  }, [linking])

  const close = () => {
    setStep({ step: 'form' })
    setServerError(null)
    setEmailError(null)
    form.reset()
    onOpenChange(false)
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (next) onOpenChange(true)
        else close()
      }}
    >
      <DialogContent className="sm:max-w-lg">
        {step.step === 'link' ? (
          <>
            <DialogHeader>
              <DialogTitle>Share this invite link</DialogTitle>
              <DialogDescription>
                For <strong>{step.invitation.email}</strong> as{' '}
                {roleInfo[step.invitation.role].label.toLowerCase()}. Expires{' '}
                <time
                  dateTime={step.invitation.expiresAt}
                  title={formatFull(step.invitation.expiresAt)}
                >
                  {formatDate(step.invitation.expiresAt)}
                </time>
                .
              </DialogDescription>
            </DialogHeader>
            <CodeBlock code={step.url} label="invite link" copyRef={copyRef} />
            <FormAlert variant="warning" title="This link won't be shown again">
              Send it to them in any chat or email. Use Resend to make a new one.
            </FormAlert>
            <DialogFooter>
              <Button onClick={close}>Done</Button>
            </DialogFooter>
          </>
        ) : step.step === 'resending' ? (
          <>
            <DialogHeader>
              <DialogTitle>Resend the invite</DialogTitle>
              <DialogDescription>
                A new link replaces the old one, which stops working.
              </DialogDescription>
            </DialogHeader>
            {step.error === null ? (
              <p aria-busy className="flex items-center gap-2 text-muted-foreground">
                <Spinner aria-hidden />
                Making a new link
              </p>
            ) : (
              <FormAlert title="Couldn't make a new link">{step.error}</FormAlert>
            )}
            <DialogFooter>
              <DialogClose render={<Button variant="outline" />}>Close</DialogClose>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Invite a teammate</DialogTitle>
              <DialogDescription>
                You get a link to share with them. It works for 7 days, only for this email.
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
              {serverError === null ? null : (
                <FormAlert title="Couldn't invite">{serverError}</FormAlert>
              )}
              <form.Field name="email">
                {(field) => {
                  const errors = [
                    ...(field.state.meta.errors as ({ message?: string } | undefined)[]),
                    ...(emailError === null ? [] : [{ message: emailError }]),
                  ]
                  const invalid = errors.length > 0
                  const id = 'invite-email'
                  return (
                    <Field data-invalid={invalid || undefined}>
                      <FieldLabel htmlFor={id}>Email</FieldLabel>
                      <Input
                        id={id}
                        type="email"
                        autoComplete="off"
                        value={field.state.value}
                        aria-invalid={invalid || undefined}
                        aria-describedby={invalid ? `${id}-error` : undefined}
                        onBlur={field.handleBlur}
                        onChange={(event) => {
                          setEmailError(null)
                          field.handleChange(event.target.value)
                        }}
                      />
                      {invalid ? <FieldError id={`${id}-error`} errors={errors} /> : null}
                    </Field>
                  )
                }}
              </form.Field>
              <form.Field name="role">
                {(field) => (
                  <FieldSet>
                    <FieldLegend id="invite-role-label" variant="label">
                      Role
                    </FieldLegend>
                    <RoleRadioGroup
                      id="invite-role"
                      labelledBy="invite-role-label"
                      value={field.state.value}
                      onChange={(role) => {
                        field.handleChange(role)
                      }}
                    />
                  </FieldSet>
                )}
              </form.Field>
              <DialogFooter>
                <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
                <form.Subscribe selector={(state) => state.isSubmitting}>
                  {(submitting) => (
                    <Button type="submit" loading={submitting}>
                      Invite
                    </Button>
                  )}
                </form.Subscribe>
              </DialogFooter>
            </form>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}
