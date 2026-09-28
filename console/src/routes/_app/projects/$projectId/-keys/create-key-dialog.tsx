import { useForm } from '@tanstack/react-form'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { z } from 'zod'

import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { CodeBlock, CopyableId } from '@/components/ui/code-block'
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { projectClient } from '@/lib/console-client'
import { describeError } from '@/lib/errors'
import { keys } from '@/lib/queries'
import { nameSchema } from '@/shell/name-dialog'
import type { ApiKey, ApiKeyScope, CreateApiKeyRequest } from '@orvano/console-client'

import { expiresAtFor, expiryOptions, todayLocal, type Expiry } from './expiry'
import { ScopeGrid } from './scope-grid'

const schema = z
  .object({
    name: nameSchema,
    scopes: z.array(z.custom<ApiKeyScope>()).min(1, 'Choose at least one scope'),
    expiry: z.custom<Expiry>(),
    customDate: z.string(),
  })
  .superRefine((value, context) => {
    if (value.expiry !== 'custom') return
    if (value.customDate === '') {
      context.addIssue({ code: 'custom', path: ['customDate'], message: 'Choose a date.' })
    } else if (value.customDate < todayLocal()) {
      context.addIssue({
        code: 'custom',
        path: ['customDate'],
        message: 'Choose today or a later date.',
      })
    }
  })

interface FormValues {
  name: string
  scopes: ApiKeyScope[]
  expiry: Expiry
  customDate: string
}

const defaults: FormValues = { name: '', scopes: [], expiry: 'never', customDate: '' }

/** The dialog's one piece of state: the form, or the reveal step holding the new secret. */
type Step = { step: 'form' } | { step: 'reveal'; secret: string; apiKey: ApiKey }

/**
 * Create key (spec 0007, AC-13 to AC-15): a form, then a reveal step that shows the secret once.
 * The secret lives only in this component's state: the mutation is reset (and kept for no time) as
 * soon as it is copied here, and Done drops it. The reveal step can't be dismissed by Escape, a click
 * outside, or a close button; only Done closes it, once you confirm you've copied the key.
 */
export function CreateKeyDialog({
  projectId,
  open,
  onOpenChange,
}: {
  projectId: string
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const [step, setStep] = useState<Step>({ step: 'form' })
  const [confirmed, setConfirmed] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)
  const copyRef = useRef<HTMLButtonElement>(null)
  const create = useMutation({
    mutationFn: (body: CreateApiKeyRequest) => projectClient(projectId).consoleApiKeys.create(body),
    // With no observer after `reset()`, the mutation leaves the cache at once (AC-15).
    gcTime: 0,
  })

  const form = useForm({
    defaultValues: defaults,
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      const expiresAt = expiresAtFor(value.expiry, value.customDate)
      try {
        const created = await create.mutateAsync({
          name: value.name.trim(),
          scopes: value.scopes,
          ...(expiresAt === undefined ? {} : { expiresAt }),
        })
        setStep({ step: 'reveal', secret: created.secret, apiKey: created.apiKey })
        create.reset()
        void queryClient.invalidateQueries({ queryKey: keys.apiKeys(projectId) })
      } catch (error) {
        create.reset()
        setServerError(describeError(error).message)
      }
    },
  })

  // The dialog stays open between steps, so focus moves by hand (AC-14).
  const revealing = step.step === 'reveal'
  useEffect(() => {
    if (revealing) copyRef.current?.focus()
  }, [revealing])

  const close = () => {
    setStep({ step: 'form' })
    setConfirmed(false)
    setServerError(null)
    form.reset()
    onOpenChange(false)
  }

  return (
    <Dialog
      open={open}
      disablePointerDismissal={revealing}
      onOpenChange={(next) => {
        // Only Done closes the reveal step (AC-14).
        if (revealing) return
        if (next) onOpenChange(true)
        else close()
      }}
    >
      <DialogContent showCloseButton={!revealing} className="sm:max-w-lg">
        {step.step === 'reveal' ? (
          <>
            <DialogHeader>
              <DialogTitle>Copy your API key</DialogTitle>
              <DialogDescription>
                Your server sends <strong>{step.apiKey.name}</strong> with the project ID on every
                call.
              </DialogDescription>
            </DialogHeader>
            <FormAlert variant="warning" title="This key won't be shown again">
              Store it somewhere safe now, such as your server&apos;s environment. If you lose it,
              delete it and create another.
            </FormAlert>
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">API key</span>
              <CodeBlock code={step.secret} label="API key" copyRef={copyRef} />
            </div>
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">Project ID</span>
              <CopyableId value={projectId} label="project ID" />
            </div>
            <label className="flex items-center gap-2">
              <Checkbox
                checked={confirmed}
                onCheckedChange={(checked) => {
                  setConfirmed(checked)
                }}
              />
              I&apos;ve copied this key and stored it safely
            </label>
            <DialogFooter>
              <Button
                disabledReason={confirmed ? undefined : "Check the box to confirm you've copied it"}
                onClick={close}
              >
                Done
              </Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Create an API key</DialogTitle>
              <DialogDescription>
                Server code uses a key to call this project. Give it only the scopes it needs.
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
                <FormAlert title="Couldn't create the key">{serverError}</FormAlert>
              )}
              <form.Field name="name">
                {(field) => {
                  const invalid = field.state.meta.errors.length > 0
                  const id = 'create-key-name'
                  return (
                    <Field data-invalid={invalid || undefined}>
                      <FieldLabel htmlFor={id}>Name</FieldLabel>
                      <Input
                        id={id}
                        autoComplete="off"
                        value={field.state.value}
                        aria-invalid={invalid || undefined}
                        aria-describedby={invalid ? `${id}-error` : undefined}
                        onBlur={field.handleBlur}
                        onChange={(event) => {
                          field.handleChange(event.target.value)
                        }}
                      />
                      {invalid ? (
                        <FieldError id={`${id}-error`} errors={field.state.meta.errors} />
                      ) : null}
                    </Field>
                  )
                }}
              </form.Field>
              <form.Field name="scopes">
                {(field) => {
                  const invalid = field.state.meta.errors.length > 0
                  const errorId = 'create-key-scopes-error'
                  return (
                    <Field data-invalid={invalid || undefined}>
                      <ScopeGrid
                        id="create-key-scope"
                        value={field.state.value}
                        onChange={(next) => {
                          field.handleChange(next)
                        }}
                        invalid={invalid}
                        describedBy={invalid ? errorId : undefined}
                      />
                      {invalid ? (
                        <FieldError id={errorId} errors={field.state.meta.errors} />
                      ) : null}
                    </Field>
                  )
                }}
              </form.Field>
              <form.Field name="expiry">
                {(field) => (
                  <Field>
                    <FieldLabel htmlFor="create-key-expiry">Expiry</FieldLabel>
                    <Select
                      items={expiryOptions}
                      value={field.state.value}
                      onValueChange={(next) => {
                        if (next !== null) field.handleChange(next)
                      }}
                    >
                      <SelectTrigger id="create-key-expiry" className="w-48">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {expiryOptions.map((option) => (
                          <SelectItem key={option.value} value={option.value}>
                            {option.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </Field>
                )}
              </form.Field>
              <form.Subscribe selector={(state) => state.values.expiry}>
                {(expiry) =>
                  expiry === 'custom' ? (
                    <form.Field name="customDate">
                      {(field) => {
                        const invalid = field.state.meta.errors.length > 0
                        const id = 'create-key-date'
                        return (
                          <Field data-invalid={invalid || undefined}>
                            <FieldLabel htmlFor={id}>Expires after</FieldLabel>
                            <Input
                              id={id}
                              type="date"
                              className="w-48"
                              min={todayLocal()}
                              value={field.state.value}
                              aria-invalid={invalid || undefined}
                              aria-describedby={`${id}-hint${invalid ? ` ${id}-error` : ''}`}
                              onBlur={field.handleBlur}
                              onChange={(event) => {
                                field.handleChange(event.target.value)
                              }}
                            />
                            <FieldDescription id={`${id}-hint`}>
                              The key stops working at the end of this day, in your time zone.
                            </FieldDescription>
                            {invalid ? (
                              <FieldError id={`${id}-error`} errors={field.state.meta.errors} />
                            ) : null}
                          </Field>
                        )
                      }}
                    </form.Field>
                  ) : null
                }
              </form.Subscribe>
              <DialogFooter>
                <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
                <form.Subscribe selector={(state) => state.isSubmitting}>
                  {(submitting) => (
                    <Button type="submit" loading={submitting}>
                      Create key
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
