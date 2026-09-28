import { useForm } from '@tanstack/react-form'
import { useState, type RefObject } from 'react'
import { z } from 'zod'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { FormAlert } from '@/components/ui/form-alert'
import { Input } from '@/components/ui/input'
import { describeError } from '@/lib/errors'

/** An org or project name: trimmed, 1 to 100 characters (spec 0003). */
export const nameSchema = z
  .string()
  .trim()
  .min(1, 'Enter a name.')
  .max(100, 'Use at most 100 characters.')

const schema = z.object({ name: nameSchema })

/**
 * A dialog that asks for one name, for Create org and Create project (spec 0007, AC-1 and AC-6). It
 * is opened by its caller, so it can live outside the switcher popup that opens it. A server error
 * shows in the form alert and keeps the dialog open.
 */
export function NameDialog({
  id,
  open,
  onOpenChange,
  finalFocus,
  title,
  description,
  submitLabel,
  errorTitle,
  onSubmit,
}: {
  /** Prefix for the field's ID, unique on the page. */
  id: string
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Where focus goes when the dialog closes; by default the element that had it before. */
  finalFocus?: RefObject<HTMLElement | null> | undefined
  title: string
  description: string
  submitLabel: string
  errorTitle: string
  /** Receives the trimmed name; resolves once the dialog may close, throws to keep it open. */
  onSubmit: (name: string) => Promise<void>
}) {
  const [serverError, setServerError] = useState<string | null>(null)
  const form = useForm({
    defaultValues: { name: '' },
    validators: { onSubmit: schema },
    onSubmit: async ({ value }) => {
      setServerError(null)
      try {
        await onSubmit(value.name.trim())
        onOpenChange(false)
        form.reset()
      } catch (error) {
        setServerError(describeError(error).message)
      }
    },
  })
  const fieldId = `${id}-name`

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        setServerError(null)
        if (!next) form.reset()
      }}
    >
      <DialogContent finalFocus={finalFocus}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <form
          noValidate
          className="flex flex-col gap-(--stack)"
          onSubmit={(event) => {
            event.preventDefault()
            void form.handleSubmit()
          }}
        >
          {serverError === null ? null : <FormAlert title={errorTitle}>{serverError}</FormAlert>}
          <form.Field name="name">
            {(field) => {
              const invalid = field.state.meta.errors.length > 0
              return (
                <Field data-invalid={invalid || undefined}>
                  <FieldLabel htmlFor={fieldId}>Name</FieldLabel>
                  <Input
                    id={fieldId}
                    autoComplete="off"
                    value={field.state.value}
                    aria-invalid={invalid || undefined}
                    aria-describedby={invalid ? `${fieldId}-error` : undefined}
                    onBlur={field.handleBlur}
                    onChange={(event) => {
                      field.handleChange(event.target.value)
                    }}
                  />
                  {invalid ? (
                    <FieldError id={`${fieldId}-error`} errors={field.state.meta.errors} />
                  ) : null}
                </Field>
              )
            }}
          </form.Field>
          <DialogFooter>
            <DialogClose render={<Button variant="outline" />}>Cancel</DialogClose>
            <form.Subscribe selector={(state) => state.isSubmitting}>
              {(submitting) => (
                <Button type="submit" loading={submitting}>
                  {submitLabel}
                </Button>
              )}
            </form.Subscribe>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
